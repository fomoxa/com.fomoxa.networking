using System;
using System.Collections.Generic;
using Fomoxa.Networking.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity
{
    public sealed class UnityPhysicsWorld2D : IPhysicsWorld2D
    {
        private readonly PhysicsScene2D physicsScene;
        private readonly List<Scene> scenes = new List<Scene>();
        private readonly Dictionary<int, Rigidbody2D> bodies = new Dictionary<int, Rigidbody2D>();
        private readonly Dictionary<Rigidbody2D, int> handles = new Dictionary<Rigidbody2D, int>();
        private readonly HashSet<int> created = new HashSet<int>();
        private readonly HashSet<Rigidbody2D> pinned = new HashSet<Rigidbody2D>();
        private readonly List<GameObject> roots = new List<GameObject>();
        private readonly List<Rigidbody2D> found = new List<Rigidbody2D>();
        private readonly HashSet<int> overlapped = new HashSet<int>();
        private Collider2D[] overlapBuffer = new Collider2D[64];
        private int nextHandle = 1;

        public UnityPhysicsWorld2D(Scene scene)
        {
            if (!scene.IsValid())
            {
                throw new ArgumentException("the scene is not valid", nameof(scene));
            }

            physicsScene = scene.GetPhysicsScene2D();
            scenes.Add(scene);
        }

        public PhysicsScene2D PhysicsScene => physicsScene;

        public IReadOnlyList<Scene> Scenes => scenes;

        public PhysicsBackend Backend => PhysicsBackend.Rigidbody;

        public BodyHandle Register(Rigidbody2D rigidbody)
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

        public BodyHandle CreateBody(in BodyDesc2D desc)
        {
            var body = new GameObject("FomoxaBody2D");
            SceneManager.MoveGameObjectToScene(body, FirstLoadedScene());
            body.transform.SetPositionAndRotation(desc.Position.ToUnity(), Quaternion.AngleAxis(ToDegrees(desc.Rotation), Vector3.forward));
            foreach (ColliderDesc2D collider in desc.Colliders)
            {
                AddCollider(body.transform, collider);
            }

            var rigidbody = body.AddComponent<Rigidbody2D>();
            rigidbody.bodyType = desc.Kind == BodyKind.Dynamic ? RigidbodyType2D.Dynamic : RigidbodyType2D.Kinematic;
            rigidbody.mass = desc.Mass > 0f ? desc.Mass : 1f;
            BodyMotion2D motion = desc.Motion;
            rigidbody.constraints = ConstraintsOf(motion.Locks);
            rigidbody.gravityScale = desc.Kind == BodyKind.Dynamic ? motion.GravityScale : 0f;
            rigidbody.linearDamping = motion.LinearDamping;
            rigidbody.angularDamping = motion.AngularDamping;
            BodyHandle handle = Register(rigidbody);
            created.Add(handle.Value);
            return handle;
        }

        internal GameObject CreateStatic(Scene scene, IReadOnlyList<ColliderDesc2D> colliders)
        {
            var group = new GameObject("FomoxaStatic2D") { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor };
            SceneManager.MoveGameObjectToScene(group, scene);
            foreach (ColliderDesc2D collider in colliders)
            {
                AddCollider(group.transform, collider);
            }

            return group;
        }

        internal GameObject ObjectOf(BodyHandle body) => Require(body).gameObject;

        public bool RemoveBody(BodyHandle body)
        {
            if (!bodies.TryGetValue(body.Value, out Rigidbody2D rigidbody))
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

            return true;
        }

        public bool Contains(BodyHandle body) => bodies.TryGetValue(body.Value, out Rigidbody2D rigidbody) && rigidbody != null;

        public BodyState2D GetBody(BodyHandle body)
        {
            Rigidbody2D rigidbody = Require(body);
            return new BodyState2D
            {
                Position = rigidbody.position.ToNumerics(),
                Rotation = ToRadians(rigidbody.rotation),
                Velocity = rigidbody.linearVelocity.ToNumerics(),
                AngularVelocity = ToRadians(rigidbody.angularVelocity),
            };
        }

        public BodyKind GetKind(BodyHandle body)
        {
            switch (Require(body).bodyType)
            {
                case RigidbodyType2D.Dynamic:
                    return BodyKind.Dynamic;
                case RigidbodyType2D.Kinematic:
                    return BodyKind.Kinematic;
                default:
                    return BodyKind.Static;
            }
        }

        public float GetMass(BodyHandle body) => Require(body).mass;

        public void SetBody(BodyHandle body, in BodyState2D state)
        {
            Rigidbody2D rigidbody = Require(body);
            Place(rigidbody, state.Position.ToUnity(), ToDegrees(state.Rotation));
            if (rigidbody.bodyType == RigidbodyType2D.Dynamic)
            {
                rigidbody.linearVelocity = state.Velocity.ToUnity();
                rigidbody.angularVelocity = ToDegrees(state.AngularVelocity);
            }

            rigidbody.WakeUp();
        }

        public void SetRewindable(BodyHandle body, bool rewindable)
        {
            Rigidbody2D rigidbody = Require(body);
            if (rewindable)
            {
                pinned.Remove(rigidbody);
            }
            else
            {
                pinned.Add(rigidbody);
            }
        }

        public void AddForce(BodyHandle body, System.Numerics.Vector2 force)
        {
            Rigidbody2D rigidbody = Require(body);
            rigidbody.AddForce(force.ToUnity(), ForceMode2D.Force);
            rigidbody.WakeUp();
        }

        public void AddImpulse(BodyHandle body, System.Numerics.Vector2 impulse)
        {
            Rigidbody2D rigidbody = Require(body);
            if (rigidbody.bodyType == RigidbodyType2D.Dynamic)
            {
                rigidbody.linearVelocity += impulse.ToUnity() / rigidbody.mass;
            }

            rigidbody.WakeUp();
        }

        public void Step(float seconds) => physicsScene.Simulate(seconds);

        public PhysicsSnapshot CreateSnapshot() => new Rigidbody2DSnapshot();

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
                    foreach (Rigidbody2D rigidbody in found)
                    {
                        if (IsRewound(rigidbody))
                        {
                            snapshot.Entries.Add(new Rigidbody2DEntry(rigidbody));
                        }
                    }
                }
            }

            roots.Clear();
            found.Clear();
        }

        public void Load(PhysicsSnapshot from)
        {
            foreach (Rigidbody2DEntry entry in Expect(from).Entries)
            {
                Rigidbody2D rigidbody = entry.Rigidbody;
                if (rigidbody == null || rigidbody.gameObject.scene.GetPhysicsScene2D() != physicsScene || !IsRewound(rigidbody))
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

        public bool Raycast(System.Numerics.Vector2 origin, System.Numerics.Vector2 direction, float maxDistance, out RayHit2D hit)
        {
            RaycastHit2D unityHit = physicsScene.Raycast(origin.ToUnity(), direction.ToUnity().normalized, maxDistance);
            if (unityHit.collider == null)
            {
                hit = default;
                return false;
            }

            hit = new RayHit2D(HandleOf(unityHit.rigidbody), unityHit.point.ToNumerics(), unityHit.normal.ToNumerics(), unityHit.distance);
            return true;
        }

        public int Overlap(System.Numerics.Vector2 center, float radius, BodyHandle[] results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            var solidOnly = new ContactFilter2D { useTriggers = false };
            int count;
            while ((count = physicsScene.OverlapCircle(center.ToUnity(), radius, solidOnly, overlapBuffer)) == overlapBuffer.Length)
            {
                overlapBuffer = new Collider2D[overlapBuffer.Length * 2];
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

        private bool IsRewound(Rigidbody2D rigidbody) =>
            rigidbody.bodyType == RigidbodyType2D.Dynamic && rigidbody.simulated && !pinned.Contains(rigidbody);

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

        private static void Place(Rigidbody2D rigidbody, Vector2 position, float degrees)
        {
            rigidbody.position = position;
            rigidbody.rotation = degrees;
            Transform placed = rigidbody.transform;
            placed.SetPositionAndRotation(new Vector3(position.x, position.y, placed.position.z), Quaternion.AngleAxis(degrees, Vector3.forward));
        }

        private static void AddCollider(Transform body, in ColliderDesc2D desc)
        {
            var part = new GameObject("FomoxaCollider2D") { layer = desc.Layer };
            part.transform.SetParent(body, false);
            part.transform.SetLocalPositionAndRotation(new Vector3(desc.Position.X, desc.Position.Y, 0f), Quaternion.AngleAxis(ToDegrees(desc.Rotation), Vector3.forward));
            Collider2D collider = AddShape(part, desc.Shape);
            collider.isTrigger = desc.IsTrigger;
            collider.sharedMaterial = ColliderMaterials.ToUnity2D(desc.Material);
        }

        private static Collider2D AddShape(GameObject part, in BodyShape2D shape)
        {
            switch (shape.Kind)
            {
                case ShapeKind2D.Box:
                    var box = part.AddComponent<BoxCollider2D>();
                    box.size = (shape.HalfExtents * 2f).ToUnity();
                    return box;
                case ShapeKind2D.Circle:
                    var circle = part.AddComponent<CircleCollider2D>();
                    circle.radius = shape.Radius;
                    return circle;
                case ShapeKind2D.Capsule:
                    var capsule = part.AddComponent<CapsuleCollider2D>();
                    capsule.direction = CapsuleDirection2D.Vertical;
                    capsule.size = new Vector2(shape.Radius * 2f, (shape.HalfHeight + shape.Radius) * 2f);
                    return capsule;
                case ShapeKind2D.ConvexPolygon:
                    var polygon = part.AddComponent<PolygonCollider2D>();
                    polygon.pathCount = 1;
                    polygon.SetPath(0, ToUnity(shape.Points));
                    return polygon;
                default:
                    var edge = part.AddComponent<EdgeCollider2D>();
                    edge.points = ToUnity(shape.Points);
                    return edge;
            }
        }

        private static RigidbodyConstraints2D ConstraintsOf(BodyLocks2D locks)
        {
            RigidbodyConstraints2D constraints = RigidbodyConstraints2D.None;
            constraints |= (locks & BodyLocks2D.PositionX) != 0 ? RigidbodyConstraints2D.FreezePositionX : RigidbodyConstraints2D.None;
            constraints |= (locks & BodyLocks2D.PositionY) != 0 ? RigidbodyConstraints2D.FreezePositionY : RigidbodyConstraints2D.None;
            constraints |= (locks & BodyLocks2D.Rotation) != 0 ? RigidbodyConstraints2D.FreezeRotation : RigidbodyConstraints2D.None;
            return constraints;
        }

        private static Vector2[] ToUnity(IReadOnlyList<System.Numerics.Vector2> points)
        {
            var converted = new Vector2[points.Count];
            for (int index = 0; index < converted.Length; index++)
            {
                converted[index] = points[index].ToUnity();
            }

            return converted;
        }

        private static float ToDegrees(float radians) => radians * Mathf.Rad2Deg;

        private static float ToRadians(float degrees) => degrees * Mathf.Deg2Rad;

        private static Rigidbody2DSnapshot Expect(PhysicsSnapshot snapshot) =>
            snapshot as Rigidbody2DSnapshot ?? throw new ArgumentException("the snapshot was not created by this backend", nameof(snapshot));

        private BodyHandle HandleOf(Rigidbody2D rigidbody) =>
            rigidbody != null && handles.TryGetValue(rigidbody, out int handle) ? new BodyHandle(handle) : default;

        private Rigidbody2D Require(BodyHandle body)
        {
            if (!bodies.TryGetValue(body.Value, out Rigidbody2D rigidbody) || rigidbody == null)
            {
                throw new ArgumentException($"body {body.Value} is not in this world", nameof(body));
            }

            return rigidbody;
        }

        private sealed class Rigidbody2DSnapshot : PhysicsSnapshot
        {
            public readonly List<Rigidbody2DEntry> Entries = new List<Rigidbody2DEntry>();
        }

        private readonly struct Rigidbody2DEntry
        {
            public Rigidbody2DEntry(Rigidbody2D rigidbody)
            {
                Rigidbody = rigidbody;
                Position = rigidbody.position;
                Rotation = rigidbody.rotation;
                Velocity = rigidbody.linearVelocity;
                AngularVelocity = rigidbody.angularVelocity;
                Sleeping = rigidbody.IsSleeping();
            }

            public Rigidbody2D Rigidbody { get; }

            public Vector2 Position { get; }

            public float Rotation { get; }

            public Vector2 Velocity { get; }

            public float AngularVelocity { get; }

            public bool Sleeping { get; }
        }
    }
}
