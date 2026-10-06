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

    internal sealed class PhysicsWorlds
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
        private readonly List<ContactTracker<Collider>> queried = new List<ContactTracker<Collider>>();
        private readonly List<ContactTracker<Collider2D>> queried2D = new List<ContactTracker<Collider2D>>();

        public PhysicsWorlds(PhysicsBackend backend)
        {
            Backend = backend;
        }

        public PhysicsBackend Backend { get; }

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

        public PhysicsHistory HistoryOf(IPhysicsSimulation world, int capacity) => histories.Of(world, capacity);

        public void StepWorlds(float seconds)
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.isLoaded)
                {
                    Of(scene);
                    Of2D(scene);
                }
            }

            queried.Clear();
            queried2D.Clear();
            Step3D(seconds);
            Step2D(seconds);
        }

        public void QueryContacts(uint tick, bool record, int capacity)
        {
            foreach (ContactTracker<Collider> tracker in queried)
            {
                tracker.Query();
                if (record)
                {
                    tracker.Record(tick, capacity);
                }
            }

            foreach (ContactTracker<Collider2D> tracker in queried2D)
            {
                tracker.Query();
                if (record)
                {
                    tracker.Record(tick, capacity);
                }
            }
        }

        public void PublishContacts()
        {
            foreach (ContactTracker<Collider> tracker in queried)
            {
                tracker.Publish();
            }

            foreach (ContactTracker<Collider2D> tracker in queried2D)
            {
                tracker.Publish();
            }

            queried.Clear();
            queried2D.Clear();
        }

        public void ReleaseStepping() => PhysicsStepOwners.ReleaseAll(this);

        public void PlaceProxy(NetworkObject networkObject)
        {
            if (!TryGetRigidbody(networkObject, out Rigidbody rigidbody))
            {
                PlaceProxy2D(networkObject);
                return;
            }

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

        public void EndProxy(NetworkObject networkObject)
        {
            if (TryGetRigidbody(networkObject, out Rigidbody rigidbody) && proxies.Remove(rigidbody, out bool wasKinematic))
            {
                rigidbody.isKinematic = wasKinematic;
                UnityPhysicsWorld world = Of(networkObject.gameObject.scene);
                world.SetRewindable(world.Register(rigidbody), true);
            }
            else if (TryGetRigidbody2D(networkObject, out Rigidbody2D rigidbody2D) && proxies2D.Remove(rigidbody2D, out RigidbodyType2D bodyType))
            {
                rigidbody2D.bodyType = bodyType;
                UnityPhysicsWorld2D world = Of2D(networkObject.gameObject.scene);
                world.SetRewindable(world.Register(rigidbody2D), true);
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
        }

        private void PlaceProxy2D(NetworkObject networkObject)
        {
            if (!TryGetRigidbody2D(networkObject, out Rigidbody2D rigidbody))
            {
                return;
            }

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

        private void Step3D(float seconds)
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

            foreach (UnityPhysicsWorld world in stepping)
            {
                world.Step(seconds);
                ContactTracker<Collider> tracker = ContactTrackers.Of(world.PhysicsScene);
                if (tracker != null)
                {
                    queried.Add(tracker);
                }
            }

            emptied.Clear();
            stepping.Clear();
        }

        private void Step2D(float seconds)
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

            foreach (UnityPhysicsWorld2D world in stepping2D)
            {
                world.Step(seconds);
                ContactTracker<Collider2D> tracker = ContactTrackers.Of(world.PhysicsScene);
                if (tracker != null)
                {
                    queried2D.Add(tracker);
                }
            }

            emptied2D.Clear();
            stepping2D.Clear();
        }

        private bool TryGetRigidbody(NetworkObject networkObject, out Rigidbody rigidbody)
        {
            rigidbody = null;
            return Backend == PhysicsBackend.Rigidbody && networkObject != null && networkObject.TryGetComponent(out rigidbody);
        }

        private bool TryGetRigidbody2D(NetworkObject networkObject, out Rigidbody2D rigidbody)
        {
            rigidbody = null;
            return Backend == PhysicsBackend.Rigidbody && networkObject != null && networkObject.TryGetComponent(out rigidbody);
        }
    }
}
