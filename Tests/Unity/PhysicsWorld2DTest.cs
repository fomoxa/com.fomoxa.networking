using System;
using System.Collections.Generic;
using BundleFixture;
using Fomoxa.Networking.Simulation;
using Fomoxa.Unity.Tests.Support;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

namespace Fomoxa.Unity.Tests
{
    public sealed class PhysicsWorld2DTest
    {
        private const uint PrefabId = 0xBE;
        private const double FrameSeconds = 1.0 / 30;

        private readonly List<GameObject> created = new List<GameObject>();
        private readonly List<NetworkManager> managers = new List<NetworkManager>();
        private Scene scene;
        private UnityPhysicsWorld2D world;
        private SimulationMode modeBefore;
        private SimulationMode2D mode2DBefore;

        [SetUp]
        public void CreateWorld()
        {
            modeBefore = Physics.simulationMode;
            mode2DBefore = Physics2D.simulationMode;
            PhysicsSimulationOwner.Acquire();
            scene = EditorSceneManager.NewPreviewScene();
            world = new UnityPhysicsWorld2D(scene);
        }

        [TearDown]
        public void DestroyWorld()
        {
            foreach (NetworkManager manager in managers)
            {
                manager.ClientManager.StopConnection();
                manager.ServerManager.StopConnection();
                manager.ReleasePhysicsSimulation();
            }

            foreach (GameObject gameObject in created)
            {
                if (gameObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }

            managers.Clear();
            created.Clear();
            TestPrefabs.DestroyAllInScene();
            EditorSceneManager.ClosePreviewScene(scene);
            PhysicsSimulationOwner.Release();
            Assert.AreEqual(modeBefore, Physics.simulationMode);
            Assert.AreEqual(mode2DBefore, Physics2D.simulationMode);
            Assert.AreEqual(0, PhysicsStepOwners.Count);
        }

        [Test]
        public void AForceLastsOneStepAndAnImpulseChangesTheVelocityAtOnce()
        {
            PhysicsBody2D body = Body(new Vector2(0f, 0f), 2f);

            body.AddImpulse(new System.Numerics.Vector2(4f, 0f));

            Assert.AreEqual(2f, body.Velocity.X, 1e-5f);

            body.AddForce(new System.Numerics.Vector2(10f, 0f));
            world.Step(0.1f);

            Assert.AreEqual(2.5f, body.Velocity.X, 1e-4f);

            world.Step(0.1f);

            Assert.AreEqual(2.5f, body.Velocity.X, 1e-4f);
            Assert.AreEqual(2f, body.Mass);
            Assert.IsFalse(body.IsKinematic);
        }

        [Test]
        public void AnImpulseIsKeptByTheNextStep()
        {
            PhysicsBody2D body = Body(new Vector2(0f, 0f), 2f);

            body.AddImpulse(new System.Numerics.Vector2(4f, 0f));
            world.Step(0.5f);

            Assert.AreEqual(2f, body.Velocity.X, 1e-4f);
            Assert.AreEqual(1f, body.Position.X, 1e-3f);
        }

        [Test]
        public void AnglesAreRadiansAtTheInterfaceAndDegreesInTheRigidbody()
        {
            Rigidbody2D rigidbody = Rigid(new Vector2(0f, 0f), 1f);
            var body = new PhysicsBody2D(world, world.Register(rigidbody));

            body.State = new BodyState2D { Position = new System.Numerics.Vector2(1f, 2f), Rotation = Mathf.PI / 2f, AngularVelocity = Mathf.PI };

            Assert.AreEqual(90f, rigidbody.rotation, 1e-3f);
            Assert.AreEqual(180f, rigidbody.angularVelocity, 1e-3f);
            Assert.AreEqual(90f, rigidbody.transform.eulerAngles.z, 1e-3f);
            Assert.AreEqual(2f, rigidbody.transform.position.y, 1e-5f);
            Assert.AreEqual(Mathf.PI / 2f, body.Rotation, 1e-5f);
            Assert.AreEqual(Mathf.PI, body.AngularVelocity, 1e-5f);
        }

        [Test]
        public void ASnapshotHoldsEachDynamicSimulatedBodyOnceAcrossRootsAndChildren()
        {
            Rigid(new Vector2(0f, 0f), 1f);
            Rigid(new Vector2(5f, 0f), 1f);
            Rigidbody2D parent = Rigid(new Vector2(10f, 0f), 1f);
            Rigidbody2D child = Rigid(new Vector2(10f, 5f), 1f);
            child.transform.SetParent(parent.transform, true);
            Rigid(new Vector2(15f, 0f), 1f).bodyType = RigidbodyType2D.Kinematic;
            Rigid(new Vector2(20f, 0f), 1f).bodyType = RigidbodyType2D.Static;
            Rigid(new Vector2(25f, 0f), 1f).simulated = false;
            world.SetRewindable(world.Register(Rigid(new Vector2(30f, 0f), 1f)), false);
            PhysicsSnapshot snapshot = world.CreateSnapshot();

            world.Save(snapshot);

            Assert.AreEqual(4, UnityPhysicsWorld2D.EntryCount(snapshot));

            world.Save(snapshot);

            Assert.AreEqual(4, UnityPhysicsWorld2D.EntryCount(snapshot));
        }

        [Test]
        public void LoadRestoresEveryDynamicBodyButNotPinnedNewOrDestroyedOnes()
        {
            PhysicsBody2D registered = Body(new Vector2(0f, 0f), 1f);
            Rigidbody2D cosmetic = Rigid(new Vector2(5f, 0f), 1f);
            Rigidbody2D pinnedRigidbody = Rigid(new Vector2(10f, 0f), 1f);
            BodyHandle pinnedHandle = world.Register(pinnedRigidbody);
            var pinned = new PhysicsBody2D(world, pinnedHandle);
            Rigidbody2D destroyed = Rigid(new Vector2(15f, 0f), 1f);
            world.SetRewindable(pinnedHandle, false);
            PhysicsSnapshot snapshot = world.CreateSnapshot();
            world.Save(snapshot);

            registered.Position = new System.Numerics.Vector2(1f, 0f);
            cosmetic.position = new Vector2(6f, 0f);
            pinned.Position = new System.Numerics.Vector2(11f, 0f);
            UnityEngine.Object.DestroyImmediate(destroyed.gameObject);
            PhysicsBody2D later = Body(new Vector2(20f, 0f), 1f);
            later.Position = new System.Numerics.Vector2(21f, 0f);
            world.Load(snapshot);

            Assert.AreEqual(0f, registered.Position.X, 1e-5f);
            Assert.AreEqual(5f, cosmetic.position.x, 1e-5f);
            Assert.AreEqual(11f, pinned.Position.X, 1e-5f);
            Assert.AreEqual(21f, later.Position.X, 1e-5f);
        }

        [Test]
        public void SettingTheStateTeleportsAndWakesTheBodyAndLoadKeepsSleep()
        {
            Rigidbody2D rigidbody = Rigid(new Vector2(0f, 0f), 1f);
            var body = new PhysicsBody2D(world, world.Register(rigidbody));
            rigidbody.Sleep();
            PhysicsSnapshot snapshot = world.CreateSnapshot();
            world.Save(snapshot);

            body.State = new BodyState2D { Position = new System.Numerics.Vector2(3f, 0f), Velocity = new System.Numerics.Vector2(1f, 0f) };

            Assert.IsFalse(rigidbody.IsSleeping());
            Assert.AreEqual(3f, rigidbody.transform.position.x, 1e-5f);
            Assert.AreEqual(1f, body.Velocity.X, 1e-5f);

            world.Load(snapshot);

            Assert.IsTrue(rigidbody.IsSleeping());
            Assert.AreEqual(0f, body.Position.X, 1e-5f);
        }

        [Test]
        public void QueriesFindRegisteredBodiesAndOverlapSkipsTriggers()
        {
            BodyHandle handle = world.Register(Rigid(new Vector2(5f, 0f), 1f));
            Rigid(new Vector2(5f, 5f), 1f);
            Rigid(new Vector2(5f, 0.2f), 1f).GetComponent<Collider2D>().isTrigger = true;
            world.Step(0f);

            Assert.IsTrue(world.Raycast(System.Numerics.Vector2.Zero, new System.Numerics.Vector2(1f, 0f), 100f, out RayHit2D hit));
            Assert.AreEqual(handle, hit.Body);
            Assert.AreEqual(4.5f, hit.Distance, 1e-3f);
            var results = new BodyHandle[4];
            Assert.AreEqual(1, world.Overlap(new System.Numerics.Vector2(5f, 0f), 1f, results));
            Assert.AreEqual(handle, results[0]);
            Assert.IsTrue(world.Raycast(new System.Numerics.Vector2(0f, 5f), new System.Numerics.Vector2(1f, 0f), 100f, out RayHit2D unregistered));
            Assert.IsFalse(unregistered.Body.IsValid);
            Assert.IsFalse(world.Raycast(System.Numerics.Vector2.Zero, new System.Numerics.Vector2(-1f, 0f), 100f, out _));
        }

        [Test]
        public void CreatedBodiesAreRemovedAndUnknownHandlesThrow()
        {
            BodyHandle handle = world.CreateBody(new BodyDesc2D(BodyKind.Kinematic, BodyShape2D.Capsule(0.5f, 1f), new System.Numerics.Vector2(1f, 2f), 0f, 3f));
            BodyHandle circle = world.CreateBody(new BodyDesc2D(BodyKind.Dynamic, BodyShape2D.Circle(0.5f), new System.Numerics.Vector2(4f, 0f), Mathf.PI, 3f));

            Assert.IsTrue(world.Contains(handle));
            Assert.AreEqual(BodyKind.Kinematic, world.GetKind(handle));
            Assert.AreEqual(BodyKind.Dynamic, world.GetKind(circle));
            Assert.AreEqual(3f, world.GetMass(circle), 1e-5f);
            Assert.AreEqual(Mathf.PI, Mathf.Abs(world.GetBody(circle).Rotation), 1e-4f);
            Assert.AreEqual(2f, world.GetBody(handle).Position.Y, 1e-5f);
            Assert.IsTrue(world.RemoveBody(handle));
            Assert.IsFalse(world.Contains(handle));
            Assert.IsFalse(world.RemoveBody(handle));
            Assert.Throws<ArgumentException>(() => world.GetBody(handle));
            Assert.IsFalse(new PhysicsBody2D(world, handle).IsValid);
            Assert.IsTrue(world.RemoveBody(circle));
        }

        [Test]
        public void TheSimulationModeOfBothKindsIsOwnedByCountAndAnApplicationChangeIsKept()
        {
            Assert.AreEqual(SimulationMode.Script, Physics.simulationMode);
            Assert.AreEqual(SimulationMode2D.Script, Physics2D.simulationMode);

            PhysicsSimulationOwner.Acquire();
            PhysicsSimulationOwner.Release();

            Assert.AreEqual(SimulationMode2D.Script, Physics2D.simulationMode);

            PhysicsSimulationOwner.Acquire();
            Physics2D.simulationMode = SimulationMode2D.Update;
            PhysicsSimulationOwner.Release();

            Assert.AreEqual(SimulationMode2D.Update, Physics2D.simulationMode);
            Physics2D.simulationMode = SimulationMode2D.Script;
        }

        [Test]
        public void ANetworkManagerThatSimulatesPhysicsStepsThe2DWorldOfItsObjectsEachTick()
        {
            NetworkObject prefab = TestPrefabs.Create("Physical2D", PrefabId);
            var prefabBody = prefab.gameObject.AddComponent<Rigidbody2D>();
            prefabBody.gravityScale = 0f;
            NetworkManager server = CreateManager(prefab, true);
            server.ServerManager.StartConnection(1);
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            server.ServerManager.Spawn(instance);
            PhysicsBody2D body = instance.Body2D;

            Assert.IsTrue(body.IsValid);
            Assert.IsFalse(instance.Body.IsValid);
            body.Velocity = new System.Numerics.Vector2(3f, 0f);
            RunFrames(30);

            Assert.AreEqual(3f, body.Position.X, 0.2f);
            Assert.AreEqual(2, PhysicsSimulationOwner.Owners);
        }

        [Test]
        public void StepWorldsStepsThe3DAnd2DWorldsOfAScene()
        {
            var worlds = new PhysicsWorlds(PhysicsBackend.Rigidbody);
            Rigidbody2D moving2D = Rigid(new Vector2(0f, 0f), 1f);
            moving2D.linearVelocity = new Vector2(1f, 0f);
            var gameObject = new GameObject("Body3D");
            created.Add(gameObject);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            var moving3D = gameObject.AddComponent<Rigidbody>();
            moving3D.useGravity = false;
            moving3D.linearVelocity = new Vector3(1f, 0f, 0f);
            worlds.Of(scene);
            worlds.Of2D(scene);
            try
            {
                PhysicsSteps.Step(worlds, 0.5f);

                Assert.AreEqual(0.5f, moving2D.position.x, 1e-3f);
                Assert.AreEqual(0.5f, moving3D.position.x, 1e-3f);
            }
            finally
            {
                worlds.ReleaseStepping();
            }
        }

        [Test]
        public void A2DWorldIsSteppedByOneOwnerAtATimeAndPassesOnWhenReleased()
        {
            var first = new PhysicsWorlds(PhysicsBackend.Rigidbody);
            var second = new PhysicsWorlds(PhysicsBackend.Rigidbody);
            Rigidbody2D moving = Rigid(new Vector2(0f, 0f), 1f);
            moving.linearVelocity = new Vector2(1f, 0f);
            first.Of2D(scene);
            second.Of2D(scene);
            try
            {
                PhysicsSteps.Step(first, 0.5f);
                PhysicsSteps.Step(second, 0.5f);

                Assert.AreEqual(0.5f, moving.position.x, 1e-3f);

                first.ReleaseStepping();
                PhysicsSteps.Step(second, 0.5f);
                PhysicsSteps.Step(first, 0.5f);

                Assert.AreEqual(1f, moving.position.x, 1e-3f);
            }
            finally
            {
                first.ReleaseStepping();
                second.ReleaseStepping();
            }
        }

        [Test]
        public void A2DProxyIsKinematicStillFollowsItsTransformIsLeftOutOfTheSnapshotAndEnds()
        {
            NetworkObject networkObject = TestPrefabs.Create("Proxy2D", PrefabId);
            created.Add(networkObject.gameObject);
            SceneManager.MoveGameObjectToScene(networkObject.gameObject, scene);
            networkObject.gameObject.AddComponent<CircleCollider2D>().radius = 0.5f;
            var rigidbody = networkObject.gameObject.AddComponent<Rigidbody2D>();
            rigidbody.gravityScale = 0f;
            rigidbody.linearVelocity = new Vector2(5f, 0f);
            rigidbody.angularVelocity = 90f;
            var worlds = new PhysicsWorlds(PhysicsBackend.Rigidbody);
            networkObject.transform.SetPositionAndRotation(new Vector3(3f, 1f, 0f), Quaternion.AngleAxis(30f, Vector3.forward));

            worlds.PlaceProxy(networkObject);

            Assert.AreEqual(RigidbodyType2D.Kinematic, rigidbody.bodyType);
            Assert.AreEqual(3f, rigidbody.position.x, 1e-5f);
            Assert.AreEqual(30f, rigidbody.rotation, 1e-3f);
            worlds.Of2D(scene).Step(0.5f);
            Assert.AreEqual(3f, rigidbody.position.x, 1e-5f);
            Assert.AreEqual(30f, rigidbody.rotation, 1e-3f);
            PhysicsSnapshot snapshot = worlds.Of2D(scene).CreateSnapshot();
            worlds.Of2D(scene).Save(snapshot);
            Assert.AreEqual(0, UnityPhysicsWorld2D.EntryCount(snapshot));

            worlds.EndProxy(networkObject);

            Assert.AreEqual(RigidbodyType2D.Dynamic, rigidbody.bodyType);
            worlds.Of2D(scene).Save(snapshot);
            Assert.AreEqual(1, UnityPhysicsWorld2D.EntryCount(snapshot));
        }

        [Test]
        public void AnObjectWithoutARigidbody2DHasNoBody2D()
        {
            NetworkObject prefab = TestPrefabs.Create("Plain2D", PrefabId);
            NetworkManager server = CreateManager(prefab, false);
            server.ServerManager.StartConnection(1);
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            server.ServerManager.Spawn(instance);

            Assert.IsFalse(instance.Body2D.IsValid);
            Assert.IsFalse(UnityEngine.Object.Instantiate(prefab).Body2D.IsValid);
        }

        private PhysicsBody2D Body(Vector2 position, float mass)
        {
            Rigidbody2D rigidbody = Rigid(position, mass);
            return new PhysicsBody2D(world, world.Register(rigidbody));
        }

        private Rigidbody2D Rigid(Vector2 position, float mass)
        {
            var gameObject = new GameObject("Body2D");
            created.Add(gameObject);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            gameObject.transform.position = position;
            gameObject.AddComponent<CircleCollider2D>().radius = 0.5f;
            var rigidbody = gameObject.AddComponent<Rigidbody2D>();
            rigidbody.gravityScale = 0f;
            rigidbody.mass = mass;
            rigidbody.position = position;
            return rigidbody;
        }

        private NetworkManager CreateManager(NetworkObject prefab, bool simulate)
        {
            var transport = new GameObject("InMemoryNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(transport.gameObject);
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("transport").objectReferenceValue = transport;
            serialized.FindProperty("simulatePhysics").boolValue = simulate;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            manager.Registry = TestObjects.Registry();
            manager.Initialize();
            manager.FindServerSceneObjects = () => new List<NetworkObject>();
            manager.FindClientSceneObjects = () => new List<NetworkObject>();
            manager.Prefabs.Register(prefab);
            created.Add(manager.gameObject);
            managers.Add(manager);
            return manager;
        }

        private void RunFrames(int count)
        {
            var now = TimeSpan.FromSeconds(1);
            for (int frame = 0; frame < count; frame++)
            {
                now += TimeSpan.FromSeconds(FrameSeconds);
                foreach (NetworkManager manager in managers)
                {
                    manager.RunFrameStart(FrameSeconds, now);
                }

                foreach (NetworkManager manager in managers)
                {
                    manager.RunFrameEnd();
                }
            }
        }
    }
}
