using System;
using System.Collections.Generic;
using Fomoxa.Networking.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity
{
    public sealed class UnityPhysicsWorld : IPhysicsWorld
    {
        private readonly PhysicsScene physicsScene;
        private readonly List<Scene> scenes = new List<Scene>();
        private readonly Dictionary<int, Rigidbody> bodies = new Dictionary<int, Rigidbody>();
        private readonly Dictionary<Rigidbody, int> handles = new Dictionary<Rigidbody, int>();
        private readonly HashSet<int> created = new HashSet<int>();
        private readonly Dictionary<int, List<Mesh>> meshes = new Dictionary<int, List<Mesh>>();
        private readonly HashSet<Rigidbody> pinned = new HashSet<Rigidbody>();
        private readonly List<GameObject> roots = new List<GameObject>();
        private readonly List<Rigidbody> found = new List<Rigidbody>();
        private readonly HashSet<int> overlapped = new HashSet<int>();
        private Collider[] overlapBuffer = new Collider[64];
        private int nextHandle = 1;

        public UnityPhysicsWorld(Scene scene)
        {
            if (!scene.IsValid())
            {
                throw new ArgumentException("the scene is not valid", nameof(scene));
            }

            physicsScene = scene.GetPhysicsScene();
            scenes.Add(scene);
        }

        public PhysicsScene PhysicsScene => physicsScene;

        public IReadOnlyList<Scene> Scenes => scenes;

        public PhysicsBackend Backend => PhysicsBackend.Rigidbody;

        public BodyHandle Register(Rigidbody rigidbody)
        {
            if (rigidbody == null)
            {
                throw new ArgumentNullException(nameof(rigidbody));
            }

            if (handles.TryGetValue(rigidbody, out int existing))
            {
                return new BodyHandle(existing);
            }

            int handle = nextHandle++;
            bodies.Add(handle, rigidbody);
            handles.Add(rigidbody, handle);
            return new BodyHandle(handle);
        }

        public BodyHandle CreateBody(in BodyDesc desc)
        {
            foreach (ColliderDesc collider in desc.Colliders)
            {
                ColliderMaterials.Check(collider.Material);
            }

            var body = new GameObject("FomoxaBody");
            SceneManager.MoveGameObjectToScene(body, FirstLoadedScene());
            body.transform.SetPositionAndRotation(desc.Position.ToUnity(), desc.Rotation.ToUnity());
            var owned = new List<Mesh>();
            foreach (ColliderDesc collider in desc.Colliders)
            {
                AddCollider(body.transform, collider, owned);
            }

            var rigidbody = body.AddComponent<Rigidbody>();
            rigidbody.mass = desc.Mass > 0f ? desc.Mass : 1f;
            rigidbody.isKinematic = desc.Kind != BodyKind.Dynamic;
            rigidbody.useGravity = desc.Kind == BodyKind.Dynamic;
            BodyHandle handle = Register(rigidbody);
            created.Add(handle.Value);
            if (owned.Count > 0)
            {
                meshes.Add(handle.Value, owned);
            }

            return handle;
        }

        public bool RemoveBody(BodyHandle body)
        {
            if (!bodies.TryGetValue(body.Value, out Rigidbody rigidbody))
            {
                return false;
            }

            bodies.Remove(body.Value);
            if (rigidbody != null)
            {
                handles.Remove(rigidbody);
                pinned.Remove(rigidbody);
                if (created.Remove(body.Value))
                {
                    UnityEngine.Object.DestroyImmediate(rigidbody.gameObject);
                }
            }

            if (meshes.Remove(body.Value, out List<Mesh> owned))
            {
                foreach (Mesh mesh in owned)
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
            }

            return true;
        }

        public bool Contains(BodyHandle body) => bodies.TryGetValue(body.Value, out Rigidbody rigidbody) && rigidbody != null;

        public BodyState GetBody(BodyHandle body)
        {
            Rigidbody rigidbody = Require(body);
            return new BodyState
            {
                Position = rigidbody.position.ToNumerics(),
                Rotation = rigidbody.rotation.ToNumerics(),
                Velocity = rigidbody.linearVelocity.ToNumerics(),
                AngularVelocity = rigidbody.angularVelocity.ToNumerics(),
            };
        }

        public BodyKind GetKind(BodyHandle body) => Require(body).isKinematic ? BodyKind.Kinematic : BodyKind.Dynamic;

        public float GetMass(BodyHandle body) => Require(body).mass;

        public void SetBody(BodyHandle body, in BodyState state)
        {
            Rigidbody rigidbody = Require(body);
            Place(rigidbody, state.Position.ToUnity(), state.Rotation.ToUnity());
            if (!rigidbody.isKinematic)
            {
                rigidbody.linearVelocity = state.Velocity.ToUnity();
                rigidbody.angularVelocity = state.AngularVelocity.ToUnity();
            }

            rigidbody.WakeUp();
        }

        public void SetRewindable(BodyHandle body, bool rewindable)
        {
            Rigidbody rigidbody = Require(body);
            if (rewindable)
            {
                pinned.Remove(rigidbody);
            }
            else
            {
                pinned.Add(rigidbody);
            }
        }

        public void AddForce(BodyHandle body, System.Numerics.Vector3 force)
        {
            Rigidbody rigidbody = Require(body);
            rigidbody.AddForce(force.ToUnity(), ForceMode.Force);
            rigidbody.WakeUp();
        }

        public void AddImpulse(BodyHandle body, System.Numerics.Vector3 impulse)
        {
            Rigidbody rigidbody = Require(body);
            if (!rigidbody.isKinematic)
            {
                rigidbody.linearVelocity += impulse.ToUnity() / rigidbody.mass;
            }

            rigidbody.WakeUp();
        }

        public void Step(float seconds) => physicsScene.Simulate(seconds);

        public PhysicsSnapshot CreateSnapshot() => new RigidbodySnapshot();

        public void Save(PhysicsSnapshot into)
        {
            var snapshot = Expect(into);
            snapshot.Entries.Clear();
            found.Clear();
            foreach (Scene scene in scenes)
            {
                if (!scene.isLoaded)
                {
                    continue;
                }

                scene.GetRootGameObjects(roots);
                foreach (GameObject root in roots)
                {
                    found.Clear();
                    root.GetComponentsInChildren(found);
                    foreach (Rigidbody rigidbody in found)
                    {
                        if (!rigidbody.isKinematic && !pinned.Contains(rigidbody))
                        {
                            snapshot.Entries.Add(new RigidbodyEntry(rigidbody));
                        }
                    }
                }
            }

            roots.Clear();
            found.Clear();
        }

        public void Load(PhysicsSnapshot from)
        {
            foreach (RigidbodyEntry entry in Expect(from).Entries)
            {
                Rigidbody rigidbody = entry.Rigidbody;
                if (rigidbody == null || rigidbody.gameObject.scene.GetPhysicsScene() != physicsScene || rigidbody.isKinematic || pinned.Contains(rigidbody))
                {
                    continue;
                }

                Place(rigidbody, entry.Position, entry.Rotation);
                rigidbody.linearVelocity = entry.Velocity;
                rigidbody.angularVelocity = entry.AngularVelocity;
                if (entry.Sleeping)
                {
                    rigidbody.Sleep();
                }
                else
                {
                    rigidbody.WakeUp();
                }
            }
        }

        public bool Raycast(System.Numerics.Vector3 origin, System.Numerics.Vector3 direction, float maxDistance, out RayHit hit)
        {
            if (!physicsScene.Raycast(origin.ToUnity(), direction.ToUnity().normalized, out RaycastHit unityHit, maxDistance))
            {
                hit = default;
                return false;
            }

            hit = new RayHit(HandleOf(unityHit.rigidbody), unityHit.point.ToNumerics(), unityHit.normal.ToNumerics(), unityHit.distance);
            return true;
        }

        public int Overlap(System.Numerics.Vector3 center, float radius, BodyHandle[] results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            int count;
            while ((count = physicsScene.OverlapSphere(center.ToUnity(), radius, overlapBuffer, Physics.AllLayers, QueryTriggerInteraction.Ignore)) == overlapBuffer.Length)
            {
                overlapBuffer = new Collider[overlapBuffer.Length * 2];
            }

            overlapped.Clear();
            int written = 0;
            for (int index = 0; index < count && written < results.Length; index++)
            {
                BodyHandle handle = HandleOf(overlapBuffer[index].attachedRigidbody);
                if (handle.IsValid && overlapped.Add(handle.Value))
                {
                    results[written++] = handle;
                }
            }

            Array.Clear(overlapBuffer, 0, count);
            return written;
        }

        internal static int EntryCount(PhysicsSnapshot snapshot) => Expect(snapshot).Entries.Count;

        internal void Include(Scene scene)
        {
            if (!scenes.Contains(scene))
            {
                scenes.Add(scene);
            }
        }

        internal bool ForgetUnloadedScenes()
        {
            scenes.RemoveAll(scene => !scene.isLoaded);
            return scenes.Count > 0;
        }

        private Scene FirstLoadedScene()
        {
            foreach (Scene scene in scenes)
            {
                if (scene.isLoaded)
                {
                    return scene;
                }
            }

            throw new InvalidOperationException("the physics world has no loaded scene");
        }

        private static void Place(Rigidbody rigidbody, Vector3 position, Quaternion rotation)
        {
            rigidbody.position = position;
            rigidbody.rotation = rotation;
            rigidbody.transform.SetPositionAndRotation(position, rotation);
        }

        private static void AddCollider(Transform body, in ColliderDesc desc, List<Mesh> owned)
        {
            var part = new GameObject("FomoxaCollider") { layer = desc.Layer };
            part.transform.SetParent(body, false);
            part.transform.SetLocalPositionAndRotation(desc.Position.ToUnity(), desc.Rotation.ToUnity());
            Collider collider = AddShape(part, desc.Shape, owned);
            collider.isTrigger = desc.IsTrigger;
            collider.sharedMaterial = ColliderMaterials.ToUnity(desc.Material);
        }

        private static Collider AddShape(GameObject part, in BodyShape shape, List<Mesh> owned)
        {
            switch (shape.Kind)
            {
                case ShapeKind.Box:
                    var box = part.AddComponent<BoxCollider>();
                    box.size = (shape.HalfExtents * 2f).ToUnity();
                    return box;
                case ShapeKind.Sphere:
                    var sphere = part.AddComponent<SphereCollider>();
                    sphere.radius = shape.Radius;
                    return sphere;
                case ShapeKind.Capsule:
                    var capsule = part.AddComponent<CapsuleCollider>();
                    capsule.radius = shape.Radius;
                    capsule.height = (shape.HalfHeight + shape.Radius) * 2f;
                    return capsule;
                case ShapeKind.ConvexHull:
                    var hull = part.AddComponent<MeshCollider>();
                    hull.convex = true;
                    hull.sharedMesh = CreateMesh(shape.Points, null, owned);
                    return hull;
                default:
                    var mesh = part.AddComponent<MeshCollider>();
                    mesh.sharedMesh = CreateMesh(shape.Points, shape.Triangles, owned);
                    return mesh;
            }
        }

        private static Mesh CreateMesh(IReadOnlyList<System.Numerics.Vector3> points, IReadOnlyList<int> triangles, List<Mesh> owned)
        {
            var vertices = new Vector3[points.Count];
            for (int index = 0; index < vertices.Length; index++)
            {
                vertices[index] = points[index].ToUnity();
            }

            var mesh = new Mesh { name = "FomoxaMesh", vertices = vertices };
            if (triangles != null)
            {
                var indices = new int[triangles.Count];
                for (int index = 0; index < indices.Length; index++)
                {
                    indices[index] = triangles[index];
                }

                mesh.triangles = indices;
            }

            owned.Add(mesh);
            return mesh;
        }

        private static RigidbodySnapshot Expect(PhysicsSnapshot snapshot) =>
            snapshot as RigidbodySnapshot ?? throw new ArgumentException("the snapshot was not created by this backend", nameof(snapshot));

        private BodyHandle HandleOf(Rigidbody rigidbody) =>
            rigidbody != null && handles.TryGetValue(rigidbody, out int handle) ? new BodyHandle(handle) : default;

        private Rigidbody Require(BodyHandle body)
        {
            if (!bodies.TryGetValue(body.Value, out Rigidbody rigidbody) || rigidbody == null)
            {
                throw new ArgumentException($"body {body.Value} is not in this world", nameof(body));
            }

            return rigidbody;
        }

        private sealed class RigidbodySnapshot : PhysicsSnapshot
        {
            public readonly List<RigidbodyEntry> Entries = new List<RigidbodyEntry>();
        }

        private readonly struct RigidbodyEntry
        {
            public RigidbodyEntry(Rigidbody rigidbody)
            {
                Rigidbody = rigidbody;
                Position = rigidbody.position;
                Rotation = rigidbody.rotation;
                Velocity = rigidbody.linearVelocity;
                AngularVelocity = rigidbody.angularVelocity;
                Sleeping = rigidbody.IsSleeping();
            }

            public Rigidbody Rigidbody { get; }

            public Vector3 Position { get; }

            public Quaternion Rotation { get; }

            public Vector3 Velocity { get; }

            public Vector3 AngularVelocity { get; }

            public bool Sleeping { get; }
        }
    }
}
