using System;
using System.Collections.Generic;
using Fomoxa.Networking.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity
{
    internal static class PhysicsSimulationOwner
    {
        private static int owners;
        private static SimulationMode original;
        private static SimulationMode2D original2D;

        public static int Owners => owners;

        public static void Acquire()
        {
            if (owners++ == 0)
            {
                original = Physics.simulationMode;
                original2D = Physics2D.simulationMode;
                Physics.simulationMode = SimulationMode.Script;
                Physics2D.simulationMode = SimulationMode2D.Script;
            }
        }

        public static void Release()
        {
            if (owners == 0)
            {
                return;
            }

            owners--;
            if (owners != 0)
            {
                return;
            }

            if (Physics.simulationMode == SimulationMode.Script)
            {
                Physics.simulationMode = original;
            }

            if (Physics2D.simulationMode == SimulationMode2D.Script)
            {
                Physics2D.simulationMode = original2D;
            }
        }
    }

    internal static class PhysicsStepOwners
    {
        public static readonly StepOwners<PhysicsScene> Worlds = new StepOwners<PhysicsScene>();
        public static readonly StepOwners<PhysicsScene2D> Worlds2D = new StepOwners<PhysicsScene2D>();

        public static int Count => Worlds.Count + Worlds2D.Count;

        public static void ReleaseAll(PhysicsWorlds claimant)
        {
            Worlds.ReleaseAll(claimant);
            Worlds2D.ReleaseAll(claimant);
        }
    }

    internal sealed class StepOwners<TPhysicsScene>
    {
        private readonly Dictionary<TPhysicsScene, PhysicsWorlds> owners = new Dictionary<TPhysicsScene, PhysicsWorlds>();
        private readonly List<TPhysicsScene> releasing = new List<TPhysicsScene>();

        public int Count => owners.Count;

        public bool Owns(TPhysicsScene physicsScene) => owners.ContainsKey(physicsScene);

        public bool TryClaim(TPhysicsScene physicsScene, PhysicsWorlds claimant)
        {
            if (owners.TryGetValue(physicsScene, out PhysicsWorlds owner))
            {
                return owner == claimant;
            }

            owners.Add(physicsScene, claimant);
            return true;
        }

        public void Release(TPhysicsScene physicsScene, PhysicsWorlds claimant)
        {
            if (owners.TryGetValue(physicsScene, out PhysicsWorlds owner) && owner == claimant)
            {
                owners.Remove(physicsScene);
            }
        }

        public void ReleaseAll(PhysicsWorlds claimant)
        {
            releasing.Clear();
            foreach (KeyValuePair<TPhysicsScene, PhysicsWorlds> entry in owners)
            {
                if (entry.Value == claimant)
                {
                    releasing.Add(entry.Key);
                }
            }

            foreach (TPhysicsScene physicsScene in releasing)
            {
                owners.Remove(physicsScene);
            }

            releasing.Clear();
        }
    }

    internal sealed class PhysicsWorlds : IPhysicsWorlds
    {
        private readonly Dictionary<PhysicsScene, UnityPhysicsWorld> worlds = new Dictionary<PhysicsScene, UnityPhysicsWorld>();
        private readonly Dictionary<PhysicsScene2D, UnityPhysicsWorld2D> worlds2D = new Dictionary<PhysicsScene2D, UnityPhysicsWorld2D>();
        private readonly PhysicsHistories histories = new PhysicsHistories();
        private readonly Dictionary<Rigidbody, bool> proxies = new Dictionary<Rigidbody, bool>();
        private readonly Dictionary<Rigidbody2D, RigidbodyType2D> proxies2D = new Dictionary<Rigidbody2D, RigidbodyType2D>();
        private readonly List<UnityPhysicsWorld> stepping = new List<UnityPhysicsWorld>();
        private readonly List<UnityPhysicsWorld2D> stepping2D = new List<UnityPhysicsWorld2D>();
        private readonly List<PhysicsScene> emptied = new List<PhysicsScene>();
        private readonly List<PhysicsScene2D> emptied2D = new List<PhysicsScene2D>();
        private readonly List<Rigidbody> leaving = new List<Rigidbody>();
        private readonly List<Rigidbody2D> leaving2D = new List<Rigidbody2D>();
        private readonly Dictionary<int, StaticObject> statics = new Dictionary<int, StaticObject>();
        private readonly List<UnownedBody> unowned = new List<UnownedBody>();
        private readonly List<UnownedBody2D> unowned2D = new List<UnownedBody2D>();
        private readonly Dictionary<NetworkObject, BodySet> sets = new Dictionary<NetworkObject, BodySet>();
        private readonly List<NetworkObject> unbound = new List<NetworkObject>();
        private readonly List<Rigidbody> treeBodies = new List<Rigidbody>();
        private readonly List<Rigidbody2D> treeBodies2D = new List<Rigidbody2D>();
        private int nextStatic = 1;

        public PhysicsBackend Backend => PhysicsBackend.Rigidbody;

        public StaticGroup AddStatic(Scene scene, IReadOnlyList<ColliderDesc> colliders)
        {
            if (colliders == null)
            {
                throw new ArgumentNullException(nameof(colliders));
            }

            var owned = new List<Mesh>();
            GameObject root = colliders.Count == 0 ? null : Of(scene).CreateStatic(scene, colliders, owned);
            return Remember(root, owned, colliders.Count);
        }

        public StaticGroup AddStatic2D(Scene scene, IReadOnlyList<ColliderDesc2D> colliders)
        {
            if (colliders == null)
            {
                throw new ArgumentNullException(nameof(colliders));
            }

            GameObject root = colliders.Count == 0 ? null : Of2D(scene).CreateStatic(scene, colliders);
            return Remember(root, null, colliders.Count);
        }

        public void RemoveStatic(StaticGroup group)
        {
            if (!statics.Remove(group.Id, out StaticObject found))
            {
                return;
            }

            if (found.Root != null)
            {
                UnityEngine.Object.DestroyImmediate(found.Root);
            }

            if (found.Meshes != null)
            {
                foreach (Mesh mesh in found.Meshes)
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
            }
        }

        public PhysicsBody AddBody(Scene scene, in BodyDesc body)
        {
            UnityPhysicsWorld world = Of(scene);
            BodyHandle handle = world.CreateBody(body);
            SceneManager.MoveGameObjectToScene(world.ObjectOf(handle), scene);
            var created = new PhysicsBody(world, handle);
            unowned.Add(new UnownedBody(created, world, handle));
            return created;
        }

        public PhysicsBody2D AddBody2D(Scene scene, in BodyDesc2D body)
        {
            UnityPhysicsWorld2D world = Of2D(scene);
            BodyHandle handle = world.CreateBody(body);
            SceneManager.MoveGameObjectToScene(world.ObjectOf(handle), scene);
            var created = new PhysicsBody2D(world, handle);
            unowned2D.Add(new UnownedBody2D(created, world, handle));
            return created;
        }

        public void RemoveBody(PhysicsBody body)
        {
            int index = unowned.FindIndex(entry => EqualityComparer<PhysicsBody>.Default.Equals(entry.Body, body));
            if (index >= 0)
            {
                unowned[index].World.RemoveBody(unowned[index].Handle);
                unowned.RemoveAt(index);
            }
        }

        public void RemoveBody2D(PhysicsBody2D body)
        {
            int index = unowned2D.FindIndex(entry => EqualityComparer<PhysicsBody2D>.Default.Equals(entry.Body, body));
            if (index >= 0)
            {
                unowned2D[index].World.RemoveBody(unowned2D[index].Handle);
                unowned2D.RemoveAt(index);
            }
        }

        public UnityPhysicsWorld Of(Scene scene)
        {
            PhysicsScene physicsScene = scene.GetPhysicsScene();
            if (!worlds.TryGetValue(physicsScene, out UnityPhysicsWorld world))
            {
                world = new UnityPhysicsWorld(scene);
                worlds.Add(physicsScene, world);
            }
            else
            {
                world.Include(scene);
            }

            return world;
        }

        public UnityPhysicsWorld2D Of2D(Scene scene)
        {
            PhysicsScene2D physicsScene = scene.GetPhysicsScene2D();
            if (!worlds2D.TryGetValue(physicsScene, out UnityPhysicsWorld2D world))
            {
                world = new UnityPhysicsWorld2D(scene);
                worlds2D.Add(physicsScene, world);
            }
            else
            {
                world.Include(scene);
            }

            return world;
        }

        public PhysicsBody BodyOf(NetworkObject networkObject)
        {
            if (!TryGetRigidbody(networkObject, out Rigidbody rigidbody))
            {
                return default;
            }

            UnityPhysicsWorld world = Of(networkObject.gameObject.scene);
            return new PhysicsBody(world, world.Register(rigidbody));
        }

        public PhysicsBody2D Body2DOf(NetworkObject networkObject)
        {
            if (!TryGetRigidbody2D(networkObject, out Rigidbody2D rigidbody))
            {
                return default;
            }

            UnityPhysicsWorld2D world = Of2D(networkObject.gameObject.scene);
            return new PhysicsBody2D(world, world.Register(rigidbody));
        }

        public IReadOnlyList<PhysicsBody> BodiesOf(NetworkObject networkObject) => SetOf(networkObject)?.Bodies ?? (IReadOnlyList<PhysicsBody>)Array.Empty<PhysicsBody>();

        public IReadOnlyList<PhysicsBody2D> Bodies2DOf(NetworkObject networkObject) => SetOf(networkObject)?.Bodies2D ?? (IReadOnlyList<PhysicsBody2D>)Array.Empty<PhysicsBody2D>();

        public PhysicsHistory HistoryOf(IPhysicsSimulation world, int capacity) => histories.Of(world, capacity);

        public void WorldsToStep(List<IPhysicsSimulation> into)
        {
            ForgetUnboundSets();
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.isLoaded)
                {
                    Of(scene);
                    Of2D(scene);
                }
            }

            Claim3D(into);
            Claim2D(into);
        }

        public IContactTracker TrackerOf(IPhysicsSimulation world)
        {
            switch (world)
            {
                case UnityPhysicsWorld world3D:
                    return ContactTrackers.Of(world3D.PhysicsScene);
                case UnityPhysicsWorld2D world2D:
                    return ContactTrackers.Of(world2D.PhysicsScene);
                default:
                    return null;
            }
        }

        public void ReleaseStepping() => PhysicsStepOwners.ReleaseAll(this);

        public void PlaceProxy(NetworkObject networkObject)
        {
            BodySet set = SetOf(networkObject);
            if (set == null)
            {
                return;
            }

            foreach (Rigidbody rigidbody in set.Rigidbodies)
            {
                if (IsProxied(networkObject, rigidbody))
                {
                    PlaceProxy(networkObject, rigidbody);
                }
            }

            foreach (Rigidbody2D rigidbody in set.Rigidbodies2D)
            {
                if (IsProxied(networkObject, rigidbody))
                {
                    PlaceProxy2D(networkObject, rigidbody);
                }
            }
        }

        public void EndProxy(NetworkObject networkObject)
        {
            BodySet set = SetOf(networkObject);
            if (set == null)
            {
                return;
            }

            foreach (Rigidbody rigidbody in set.Rigidbodies)
            {
                if (rigidbody != null && proxies.Remove(rigidbody, out bool wasKinematic))
                {
                    rigidbody.isKinematic = wasKinematic;
                    UnityPhysicsWorld world = Of(networkObject.gameObject.scene);
                    world.SetRewindable(world.Register(rigidbody), true);
                }
            }

            foreach (Rigidbody2D rigidbody in set.Rigidbodies2D)
            {
                if (rigidbody != null && proxies2D.Remove(rigidbody, out RigidbodyType2D bodyType))
                {
                    rigidbody.bodyType = bodyType;
                    UnityPhysicsWorld2D world = Of2D(networkObject.gameObject.scene);
                    world.SetRewindable(world.Register(rigidbody), true);
                }
            }
        }

        public void ForgetDestroyedProxies()
        {
            leaving.Clear();
            foreach (Rigidbody rigidbody in proxies.Keys)
            {
                if (rigidbody == null)
                {
                    leaving.Add(rigidbody);
                }
            }

            foreach (Rigidbody rigidbody in leaving)
            {
                proxies.Remove(rigidbody);
            }

            leaving.Clear();
            leaving2D.Clear();
            foreach (Rigidbody2D rigidbody in proxies2D.Keys)
            {
                if (rigidbody == null)
                {
                    leaving2D.Add(rigidbody);
                }
            }

            foreach (Rigidbody2D rigidbody in leaving2D)
            {
                proxies2D.Remove(rigidbody);
            }

            leaving2D.Clear();
            ForgetUnboundSets();
        }

        private BodySet SetOf(NetworkObject networkObject)
        {
            if (networkObject == null)
            {
                return null;
            }

            if (sets.TryGetValue(networkObject, out BodySet set))
            {
                return set;
            }

            set = new BodySet();
            Transform root = networkObject.transform;
            Scene scene = networkObject.gameObject.scene;
            networkObject.GetComponentsInChildren(true, treeBodies);
            foreach (Rigidbody rigidbody in treeBodies)
            {
                if (ActiveInTree(rigidbody.transform, root))
                {
                    UnityPhysicsWorld world = Of(scene);
                    set.Rigidbodies.Add(rigidbody);
                    set.Bodies.Add(new PhysicsBody(world, world.Register(rigidbody)));
                }
            }

            networkObject.GetComponentsInChildren(true, treeBodies2D);
            foreach (Rigidbody2D rigidbody in treeBodies2D)
            {
                if (ActiveInTree(rigidbody.transform, root))
                {
                    UnityPhysicsWorld2D world = Of2D(scene);
                    set.Rigidbodies2D.Add(rigidbody);
                    set.Bodies2D.Add(new PhysicsBody2D(world, world.Register(rigidbody)));
                }
            }

            treeBodies.Clear();
            treeBodies2D.Clear();
            sets.Add(networkObject, set);
            return set;
        }

        private void ForgetUnboundSets()
        {
            unbound.Clear();
            foreach (NetworkObject networkObject in sets.Keys)
            {
                if (networkObject == null || !networkObject.IsSpawned)
                {
                    unbound.Add(networkObject);
                }
            }

            foreach (NetworkObject networkObject in unbound)
            {
                sets.Remove(networkObject);
            }

            unbound.Clear();
        }

        private static bool ActiveInTree(Transform node, Transform root)
        {
            for (Transform current = node; current != root; current = current.parent)
            {
                if (!current.gameObject.activeSelf)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsProxied(NetworkObject networkObject, Component body) =>
            body != null && (body.gameObject == networkObject.gameObject || body.TryGetComponent(out NetworkTransform _));

        private void PlaceProxy(NetworkObject networkObject, Rigidbody rigidbody)
        {
            if (!proxies.ContainsKey(rigidbody))
            {
                proxies.Add(rigidbody, rigidbody.isKinematic);
                rigidbody.isKinematic = true;
            }

            UnityPhysicsWorld world = Of(networkObject.gameObject.scene);
            BodyHandle handle = world.Register(rigidbody);
            world.SetRewindable(handle, false);
            Transform placed = rigidbody.transform;
            world.SetBody(handle, new BodyState
            {
                Position = placed.position.ToNumerics(),
                Rotation = placed.rotation.ToNumerics(),
            });
        }

        private void PlaceProxy2D(NetworkObject networkObject, Rigidbody2D rigidbody)
        {
            if (!proxies2D.ContainsKey(rigidbody))
            {
                proxies2D.Add(rigidbody, rigidbody.bodyType);
                rigidbody.bodyType = RigidbodyType2D.Kinematic;
                rigidbody.linearVelocity = Vector2.zero;
                rigidbody.angularVelocity = 0f;
            }

            UnityPhysicsWorld2D world = Of2D(networkObject.gameObject.scene);
            BodyHandle handle = world.Register(rigidbody);
            world.SetRewindable(handle, false);
            Transform placed = rigidbody.transform;
            world.SetBody(handle, new BodyState2D
            {
                Position = ((Vector2)placed.position).ToNumerics(),
                Rotation = placed.eulerAngles.z * Mathf.Deg2Rad,
            });
        }

        private void Claim3D(List<IPhysicsSimulation> into)
        {
            emptied.Clear();
            stepping.Clear();
            foreach (KeyValuePair<PhysicsScene, UnityPhysicsWorld> entry in worlds)
            {
                if (!entry.Value.ForgetUnloadedScenes())
                {
                    emptied.Add(entry.Key);
                }
                else if (PhysicsStepOwners.Worlds.TryClaim(entry.Key, this))
                {
                    stepping.Add(entry.Value);
                }
            }

            foreach (PhysicsScene physicsScene in emptied)
            {
                histories.Forget(worlds[physicsScene]);
                worlds.Remove(physicsScene);
                PhysicsStepOwners.Worlds.Release(physicsScene, this);
            }

            into.AddRange(stepping);

            emptied.Clear();
            stepping.Clear();
        }

        private void Claim2D(List<IPhysicsSimulation> into)
        {
            emptied2D.Clear();
            stepping2D.Clear();
            foreach (KeyValuePair<PhysicsScene2D, UnityPhysicsWorld2D> entry in worlds2D)
            {
                if (!entry.Value.ForgetUnloadedScenes())
                {
                    emptied2D.Add(entry.Key);
                }
                else if (PhysicsStepOwners.Worlds2D.TryClaim(entry.Key, this))
                {
                    stepping2D.Add(entry.Value);
                }
            }

            foreach (PhysicsScene2D physicsScene in emptied2D)
            {
                histories.Forget(worlds2D[physicsScene]);
                worlds2D.Remove(physicsScene);
                PhysicsStepOwners.Worlds2D.Release(physicsScene, this);
            }

            into.AddRange(stepping2D);

            emptied2D.Clear();
            stepping2D.Clear();
        }

        private static bool TryGetRigidbody(NetworkObject networkObject, out Rigidbody rigidbody)
        {
            rigidbody = null;
            return networkObject != null && networkObject.TryGetComponent(out rigidbody);
        }

        private static bool TryGetRigidbody2D(NetworkObject networkObject, out Rigidbody2D rigidbody)
        {
            rigidbody = null;
            return networkObject != null && networkObject.TryGetComponent(out rigidbody);
        }

        private StaticGroup Remember(GameObject root, List<Mesh> meshes, int count)
        {
            int id = nextStatic++;
            statics.Add(id, new StaticObject(root, meshes));
            return new StaticGroup(0, id, 0, count);
        }

        private sealed class BodySet
        {
            public readonly List<Rigidbody> Rigidbodies = new List<Rigidbody>();
            public readonly List<Rigidbody2D> Rigidbodies2D = new List<Rigidbody2D>();
            public readonly List<PhysicsBody> Bodies = new List<PhysicsBody>();
            public readonly List<PhysicsBody2D> Bodies2D = new List<PhysicsBody2D>();
        }

        private readonly struct StaticObject
        {
            public StaticObject(GameObject root, List<Mesh> meshes)
            {
                Root = root;
                Meshes = meshes;
            }

            public GameObject Root { get; }

            public List<Mesh> Meshes { get; }
        }

        private readonly struct UnownedBody
        {
            public UnownedBody(PhysicsBody body, UnityPhysicsWorld world, BodyHandle handle)
            {
                Body = body;
                World = world;
                Handle = handle;
            }

            public PhysicsBody Body { get; }

            public UnityPhysicsWorld World { get; }

            public BodyHandle Handle { get; }
        }

        private readonly struct UnownedBody2D
        {
            public UnownedBody2D(PhysicsBody2D body, UnityPhysicsWorld2D world, BodyHandle handle)
            {
                Body = body;
                World = world;
                Handle = handle;
            }

            public PhysicsBody2D Body { get; }

            public UnityPhysicsWorld2D World { get; }

            public BodyHandle Handle { get; }
        }
    }
}
