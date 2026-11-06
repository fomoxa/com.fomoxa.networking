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
        private RaycastHit2D[] castBuffer = new RaycastHit2D[64];
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

        public bool Raycast(System.Numerics.Vector2 origin, System.Numerics.Vector2 direction, float maxDistance, in QueryFilter filter, out RayHit2D hit)
        {
            Vector2 start = origin.ToUnity();
            Vector2 along = direction.ToUnity().normalized;
            if (along == Vector2.zero)
            {
                hit = default;
                return false;
            }

            ContactFilter2D contacts = ContactsOf(filter);
            int inside;
            while ((inside = physicsScene.OverlapPoint(start, contacts, overlapBuffer)) == overlapBuffer.Length)
            {
                overlapBuffer = new Collider2D[overlapBuffer.Length * 2];
            }

            if (StartsInside(inside, start, along, filter, out hit))
            {
                return true;
            }

            int count;
            while ((count = physicsScene.Raycast(start, along, maxDistance, contacts, castBuffer)) == castBuffer.Length)
            {
                castBuffer = new RaycastHit2D[castBuffer.Length * 2];
            }

            return Nearest(count, start, along, filter, out hit);
        }

        public bool ShapeCast(in BodyShape2D shape, System.Numerics.Vector2 position, float rotation, System.Numerics.Vector2 direction, float maxDistance, in QueryFilter filter, out RayHit2D hit)
        {
            CheckQueryShape(shape);
            Vector2 start = position.ToUnity();
            float angle = ToDegrees(rotation);
            Vector2 along = direction.ToUnity().normalized;
            if (along == Vector2.zero)
            {
                hit = default;
                return false;
            }

            ContactFilter2D contacts = ContactsOf(filter);
            int inside = OverlapColliders(shape, start, angle, contacts);
            if (StartsInside(inside, start, along, filter, out hit))
            {
                return true;
            }

            int count;
            while ((count = CastOnce(shape, start, angle, along, maxDistance, contacts)) == castBuffer.Length)
            {
                castBuffer = new RaycastHit2D[castBuffer.Length * 2];
            }

            return Nearest(count, start, along, filter, out hit);
        }

        public int Overlap(in BodyShape2D shape, System.Numerics.Vector2 position, float rotation, in QueryFilter filter, BodyHandle[] results)
        {
            CheckQueryShape(shape);
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            int count = OverlapColliders(shape, position.ToUnity(), ToDegrees(rotation), ContactsOf(filter));
            overlapped.Clear();
            int written = 0;
            for (int index = 0; index < count && written < results.Length; index++)
            {
                BodyHandle handle = HandleOf(overlapBuffer[index].attachedRigidbody);
                if (handle.IsValid && !Excluded(handle, filter) && overlapped.Add(handle.Value))
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
                    capsule.size = CapsuleSize(shape);
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

        private int OverlapColliders(in BodyShape2D shape, Vector2 position, float angle, ContactFilter2D contacts)
        {
            int count;
            while ((count = OverlapOnce(shape, position, angle, contacts)) == overlapBuffer.Length)
            {
                overlapBuffer = new Collider2D[overlapBuffer.Length * 2];
            }

            return count;
        }

        private int OverlapOnce(in BodyShape2D shape, Vector2 position, float angle, ContactFilter2D contacts)
        {
            switch (shape.Kind)
            {
                case ShapeKind2D.Box:
                    return physicsScene.OverlapBox(position, shape.HalfExtents.ToUnity() * 2f, angle, contacts, overlapBuffer);
                case ShapeKind2D.Circle:
                    return physicsScene.OverlapCircle(position, shape.Radius, contacts, overlapBuffer);
                default:
                    return physicsScene.OverlapCapsule(position, CapsuleSize(shape), CapsuleDirection2D.Vertical, angle, contacts, overlapBuffer);
            }
        }

        private int CastOnce(in BodyShape2D shape, Vector2 position, float angle, Vector2 along, float maxDistance, ContactFilter2D contacts)
        {
            switch (shape.Kind)
            {
                case ShapeKind2D.Box:
                    return physicsScene.BoxCast(position, shape.HalfExtents.ToUnity() * 2f, angle, along, maxDistance, contacts, castBuffer);
                case ShapeKind2D.Circle:
                    return physicsScene.CircleCast(position, shape.Radius, along, maxDistance, contacts, castBuffer);
                default:
                    return physicsScene.CapsuleCast(position, CapsuleSize(shape), CapsuleDirection2D.Vertical, angle, along, maxDistance, contacts, castBuffer);
            }
        }

        private bool StartsInside(int count, Vector2 start, Vector2 along, in QueryFilter filter, out RayHit2D hit)
        {
            hit = default;
            bool found = false;
            for (int index = 0; index < count && !found; index++)
            {
                BodyHandle body = HandleOf(overlapBuffer[index].attachedRigidbody);
                if (!Excluded(body, filter))
                {
                    hit = new RayHit2D(body, start.ToNumerics(), (-along).ToNumerics(), 0f);
                    found = true;
                }
            }

            Array.Clear(overlapBuffer, 0, count);
            return found;
        }

        private bool Nearest(int count, Vector2 start, Vector2 along, in QueryFilter filter, out RayHit2D hit)
        {
            int best = -1;
            for (int index = 0; index < count; index++)
            {
                if (!Excluded(HandleOf(castBuffer[index].rigidbody), filter) && (best < 0 || castBuffer[index].distance < castBuffer[best].distance))
                {
                    best = index;
                }
            }

            if (best < 0)
            {
                hit = default;
            }
            else
            {
                RaycastHit2D found = castBuffer[best];
                hit = found.distance > 0f
                    ? new RayHit2D(HandleOf(found.rigidbody), found.point.ToNumerics(), found.normal.ToNumerics(), found.distance)
                    : new RayHit2D(HandleOf(found.rigidbody), start.ToNumerics(), (-along).ToNumerics(), 0f);
            }

            Array.Clear(castBuffer, 0, count);
            return best >= 0;
        }

        private static bool Excluded(BodyHandle body, in QueryFilter filter) => filter.ExcludeBody.IsValid && body.Equals(filter.ExcludeBody);

        private static ContactFilter2D ContactsOf(in QueryFilter filter)
        {
            var contacts = new ContactFilter2D { useTriggers = filter.IncludeTriggers };
            contacts.SetLayerMask(filter.LayerMask);
            return contacts;
        }

        private static Vector2 CapsuleSize(in BodyShape2D shape) => new Vector2(shape.Radius * 2f, (shape.HalfHeight + shape.Radius) * 2f);

        private static void CheckQueryShape(in BodyShape2D shape)
        {
            if (shape.Kind != ShapeKind2D.Box && shape.Kind != ShapeKind2D.Circle && shape.Kind != ShapeKind2D.Capsule)
            {
                throw new ArgumentException($"a {shape.Kind} cannot be a query shape; use a box, a circle or a capsule", nameof(shape));
            }
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
