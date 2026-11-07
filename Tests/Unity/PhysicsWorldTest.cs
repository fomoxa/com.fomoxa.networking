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
using Vector3 = UnityEngine.Vector3;

namespace Fomoxa.Unity.Tests
{
    public sealed class PhysicsWorldTest
    {
        private const uint PrefabId = 0xBD;
        private const string SharedScenePath = "Assets/FomoxaSharedPhysicsTest.unity";
        private const double FrameSeconds = 1.0 / 30;

        private readonly List<GameObject> created = new List<GameObject>();
        private readonly List<NetworkManager> managers = new List<NetworkManager>();
        private Scene scene;
        private UnityPhysicsWorld world;
        private SimulationMode modeBefore;

        [SetUp]
        public void CreateWorld()
        {
            modeBefore = Physics.simulationMode;
            PhysicsSimulationOwner.Acquire();
            scene = EditorSceneManager.NewPreviewScene();
            world = new UnityPhysicsWorld(scene);
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
            Assert.AreEqual(0, PhysicsStepOwners.Count);
        }

        [Test]
        public void AForceLastsOneStepAndAnImpulseChangesTheVelocityAtOnce()
        {
            PhysicsBody body = Body(new Vector3(0f, 0f, 0f), 2f);

            body.AddImpulse(new System.Numerics.Vector3(4f, 0f, 0f));

            Assert.AreEqual(2f, body.Velocity.X, 1e-5f);

            body.AddForce(new System.Numerics.Vector3(10f, 0f, 0f));
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
            PhysicsBody body = Body(new Vector3(0f, 0f, 0f), 2f);

            body.AddImpulse(new System.Numerics.Vector3(4f, 0f, 0f));
            world.Step(0.5f);

            Assert.AreEqual(2f, body.Velocity.X, 1e-4f);
            Assert.AreEqual(1f, body.Position.X, 1e-3f);
        }

        [Test]
        public void ASnapshotHoldsEachDynamicBodyOnceAcrossRootsAndChildren()
        {
            Rigid(new Vector3(0f, 0f, 0f), 1f);
            Rigid(new Vector3(5f, 0f, 0f), 1f);
            Rigidbody parent = Rigid(new Vector3(10f, 0f, 0f), 1f);
            Rigidbody child = Rigid(new Vector3(10f, 5f, 0f), 1f);
            child.transform.SetParent(parent.transform, true);
            Rigidbody kinematic = Rigid(new Vector3(15f, 0f, 0f), 1f);
            kinematic.isKinematic = true;
            world.SetRewindable(world.Register(Rigid(new Vector3(20f, 0f, 0f), 1f)), false);
            PhysicsSnapshot snapshot = world.CreateSnapshot();

            world.Save(snapshot);

            Assert.AreEqual(4, UnityPhysicsWorld.EntryCount(snapshot));

            world.Save(snapshot);

            Assert.AreEqual(4, UnityPhysicsWorld.EntryCount(snapshot));
        }

        [Test]
        public void LoadRestoresEveryDynamicBodyButNotPinnedNewOrDestroyedOnes()
        {
            PhysicsBody registered = Body(new Vector3(0f, 0f, 0f), 1f);
            Rigidbody cosmetic = Rigid(new Vector3(5f, 0f, 0f), 1f);
            Rigidbody pinnedRigidbody = Rigid(new Vector3(10f, 0f, 0f), 1f);
            BodyHandle pinnedHandle = world.Register(pinnedRigidbody);
            var pinned = new PhysicsBody(world, pinnedHandle);
            Rigidbody destroyed = Rigid(new Vector3(15f, 0f, 0f), 1f);
            world.SetRewindable(pinnedHandle, false);
            PhysicsSnapshot snapshot = world.CreateSnapshot();
            world.Save(snapshot);

            registered.Position = new System.Numerics.Vector3(1f, 0f, 0f);
            cosmetic.position = new Vector3(6f, 0f, 0f);
            pinned.Position = new System.Numerics.Vector3(11f, 0f, 0f);
            UnityEngine.Object.DestroyImmediate(destroyed.gameObject);
            PhysicsBody later = Body(new Vector3(20f, 0f, 0f), 1f);
            later.Position = new System.Numerics.Vector3(21f, 0f, 0f);
            world.Load(snapshot);

            Assert.AreEqual(0f, registered.Position.X, 1e-5f);
            Assert.AreEqual(5f, cosmetic.position.x, 1e-5f);
            Assert.AreEqual(11f, pinned.Position.X, 1e-5f);
            Assert.AreEqual(21f, later.Position.X, 1e-5f);
        }

        [Test]
        public void SettingTheStateTeleportsAndWakesTheBodyAndLoadKeepsSleep()
        {
            Rigidbody rigidbody = Rigid(new Vector3(0f, 0f, 0f), 1f);
            var body = new PhysicsBody(world, world.Register(rigidbody));
            rigidbody.Sleep();
            PhysicsSnapshot snapshot = world.CreateSnapshot();
            world.Save(snapshot);

            body.State = new BodyState { Position = new System.Numerics.Vector3(3f, 0f, 0f), Rotation = System.Numerics.Quaternion.Identity, Velocity = new System.Numerics.Vector3(1f, 0f, 0f) };

            Assert.IsFalse(rigidbody.IsSleeping());
            Assert.AreEqual(3f, rigidbody.transform.position.x, 1e-5f);
            Assert.AreEqual(1f, body.Velocity.X, 1e-5f);

            world.Load(snapshot);

            Assert.IsTrue(rigidbody.IsSleeping());
            Assert.AreEqual(0f, body.Position.X, 1e-5f);
        }

        [Test]
        public void QueriesFindRegisteredBodies()
        {
            BodyHandle handle = world.Register(Rigid(new Vector3(0f, 0f, 5f), 1f));
            Rigid(new Vector3(0f, 5f, 5f), 1f);
            world.Step(0f);

            Assert.IsTrue(world.Raycast(System.Numerics.Vector3.Zero, new System.Numerics.Vector3(0f, 0f, 1f), 100f, out RayHit hit));
            Assert.AreEqual(handle, hit.Body);
            Assert.AreEqual(4.5f, hit.Distance, 1e-3f);
            var results = new BodyHandle[4];
            Assert.AreEqual(1, world.Overlap(new System.Numerics.Vector3(0f, 0f, 5f), 1f, results));
            Assert.AreEqual(handle, results[0]);
            Assert.IsTrue(world.Raycast(new System.Numerics.Vector3(0f, 5f, 0f), new System.Numerics.Vector3(0f, 0f, 1f), 100f, out RayHit unregistered));
            Assert.IsFalse(unregistered.Body.IsValid);
            Assert.IsFalse(world.Raycast(System.Numerics.Vector3.Zero, new System.Numerics.Vector3(0f, 1f, 0f), 100f, out _));
        }

        [Test]
        public void FiltersChooseLayersAndTriggersAndSkipABody()
        {
            BodyHandle trigger = Cube(new Vector3(0f, 0f, 2f), 0, true);
            BodyHandle near = Cube(new Vector3(0f, 0f, 5f), 0, false);
            BodyHandle far = Cube(new Vector3(0f, 0f, 8f), 3, false);
            world.Step(0f);
            var solid = new QueryFilter(-1, default, false);
            var results = new BodyHandle[4];

            AssertRay(QueryFilter.All, trigger, 1.5f);
            AssertRay(solid, near, 4.5f);
            AssertRay(new QueryFilter(-1, near, false), far, 7.5f);
            AssertRay(new QueryFilter(1 << 3, default, true), far, 7.5f);
            Assert.IsFalse(world.Raycast(System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitZ, 100f, new QueryFilter(1 << 4, default, true), out _));
            BodyShape column = BodyShape.Box(new System.Numerics.Vector3(1f, 1f, 5f));
            var middle = new System.Numerics.Vector3(0f, 0f, 5f);
            Assert.AreEqual(3, world.Overlap(column, middle, System.Numerics.Quaternion.Identity, QueryFilter.All, results));
            Assert.AreEqual(2, world.Overlap(column, middle, System.Numerics.Quaternion.Identity, solid, results));
            Assert.AreEqual(1, world.Overlap(column, middle, System.Numerics.Quaternion.Identity, new QueryFilter(1, near, true), results));
            Assert.AreEqual(trigger, results[0]);
            Assert.AreEqual(3, world.Overlap(System.Numerics.Vector3.Zero, 10f, results));
        }

        [Test]
        public void ShapeCastsAndOverlapsTakeABoxASphereAndACapsule()
        {
            BodyHandle target = Cube(new Vector3(0f, 0f, 5f), 0, false);
            world.Step(0f);
            System.Numerics.Quaternion lying = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitX, MathF.PI / 2f);
            var shapes = new[]
            {
                (BodyShape.Box(new System.Numerics.Vector3(0.5f, 0.5f, 0.5f)), System.Numerics.Quaternion.Identity, 4f),
                (BodyShape.Sphere(0.5f), System.Numerics.Quaternion.Identity, 4f),
                (BodyShape.Capsule(0.5f, 0.5f), lying, 3.5f),
            };
            var results = new BodyHandle[4];

            foreach ((BodyShape shape, System.Numerics.Quaternion rotation, float distance) in shapes)
            {
                Assert.IsTrue(world.ShapeCast(shape, System.Numerics.Vector3.Zero, rotation, System.Numerics.Vector3.UnitZ, 100f, QueryFilter.All, out RayHit hit), shape.Kind.ToString());
                Assert.AreEqual(target, hit.Body);
                Assert.AreEqual(distance, hit.Distance, 1e-2f, shape.Kind.ToString());
                Assert.AreEqual(-1f, hit.Normal.Z, 1e-3f);
                Assert.AreEqual(1, world.Overlap(shape, new System.Numerics.Vector3(0f, 0f, 4.2f), rotation, QueryFilter.All, results), shape.Kind.ToString());
                Assert.AreEqual(0, world.Overlap(shape, new System.Numerics.Vector3(0f, 2f, 4.2f), rotation, QueryFilter.All, results), shape.Kind.ToString());
            }

            BodyShape hull = BodyShape.ConvexHull(new[] { System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitX, System.Numerics.Vector3.UnitY, System.Numerics.Vector3.UnitZ });
            Assert.Throws<ArgumentException>(() => world.Overlap(hull, System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, QueryFilter.All, results));
            Assert.Throws<ArgumentException>(() => world.ShapeCast(hull, System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, System.Numerics.Vector3.UnitZ, 1f, QueryFilter.All, out _));
        }

        [Test]
        public void AQueryStartingInsideAColliderHitsItAtDistanceZero()
        {
            BodyHandle inside = Cube(new Vector3(0f, 0f, 0f), 0, false);
            BodyHandle beyond = Cube(new Vector3(0f, 0f, 5f), 0, false);
            world.Step(0f);
            var origin = new System.Numerics.Vector3(0f, 0f, 0.1f);

            Assert.IsTrue(world.Raycast(origin, System.Numerics.Vector3.UnitZ, 100f, QueryFilter.All, out RayHit ray));
            Assert.IsTrue(world.ShapeCast(BodyShape.Sphere(0.2f), origin, System.Numerics.Quaternion.Identity, System.Numerics.Vector3.UnitZ, 100f, QueryFilter.All, out RayHit cast));
            Assert.IsTrue(world.Raycast(origin, System.Numerics.Vector3.UnitZ, 100f, new QueryFilter(-1, inside, true), out RayHit skipped));

            Assert.AreEqual((inside, 0f), (ray.Body, ray.Distance));
            Assert.AreEqual((inside, 0f), (cast.Body, cast.Distance));
            Assert.AreEqual(beyond, skipped.Body);
            Assert.AreEqual(4.4f, skipped.Distance, 1e-3f);
        }

        [Test]
        public void QueriesSeeABodySetThisTickAndAfterLoad()
        {
            BodyHandle body = world.CreateBody(new BodyDesc(BodyKind.Dynamic, new[] { CubeCollider(0, false) }, new System.Numerics.Vector3(0f, 0f, 5f), System.Numerics.Quaternion.Identity, 1f, new Fomoxa.Networking.Simulation.BodyMotion(BodyLocks.None, false, 0f, 0f)));
            world.Step(0f);
            PhysicsSnapshot snapshot = world.CreateSnapshot();
            world.Save(snapshot);

            world.SetBody(body, new BodyState { Position = new System.Numerics.Vector3(0f, 0f, 10f), Rotation = System.Numerics.Quaternion.Identity });
            AssertRay(QueryFilter.All, body, 9.5f);
            world.Load(snapshot);
            AssertRay(QueryFilter.All, body, 4.5f);
        }

        [Test]
        public void ImpulsesChangeTheVelocityAtOnceAndForcesAndTorquesActAtTheNextStep()
        {
            BodyHandle spun = Ball(BodyKind.Dynamic, false, 0f);
            BodyHandle struck = Ball(BodyKind.Dynamic, false, 10f);
            BodyHandle twisted = Ball(BodyKind.Dynamic, false, 20f);
            BodyHandle pushed = Ball(BodyKind.Dynamic, false, 30f);
            BodyHandle carried = Ball(BodyKind.Kinematic, false, 40f);
            world.Step(0f);
            float inertia = world.ObjectOf(spun).GetComponent<Rigidbody>().inertiaTensor.z;

            world.AddTorqueImpulse(spun, new System.Numerics.Vector3(0f, 0f, 0.5f));
            world.AddImpulseAtPoint(struck, new System.Numerics.Vector3(0f, 0f, 1f), world.GetBody(struck).Position + System.Numerics.Vector3.UnitX);
            world.AddTorque(twisted, new System.Numerics.Vector3(0f, 0f, 1f));
            world.AddForceAtPoint(pushed, new System.Numerics.Vector3(0f, 0f, 1f), world.GetBody(pushed).Position + System.Numerics.Vector3.UnitX);
            world.AddTorqueImpulse(carried, new System.Numerics.Vector3(0f, 0f, 0.5f));

            Assert.AreEqual(0.5f / inertia, world.GetBody(spun).AngularVelocity.Z, 1e-4f);
            Assert.AreEqual(0.5f, world.GetBody(struck).Velocity.Z, 1e-5f);
            Assert.AreEqual(-1f / inertia, world.GetBody(struck).AngularVelocity.Y, 1e-4f);
            Assert.AreEqual(System.Numerics.Vector3.Zero, world.GetBody(twisted).AngularVelocity);
            Assert.AreEqual(System.Numerics.Vector3.Zero, world.GetBody(pushed).Velocity);
            Assert.AreEqual(System.Numerics.Vector3.Zero, world.GetBody(carried).AngularVelocity);

            world.Step(0.5f);

            Assert.AreEqual(0.5f / inertia, world.GetBody(twisted).AngularVelocity.Z, 1e-3f);
            Assert.AreEqual(0.25f, world.GetBody(pushed).Velocity.Z, 1e-4f);
            Assert.AreEqual(-0.5f / inertia, world.GetBody(pushed).AngularVelocity.Y, 1e-3f);
        }

        [Test]
        public void ContinuousCollisionChoosesTheDetectionModeOfACreatedBody()
        {
            Assert.AreEqual(CollisionDetectionMode.ContinuousDynamic, world.ObjectOf(Ball(BodyKind.Dynamic, true, 0f)).GetComponent<Rigidbody>().collisionDetectionMode);
            Assert.AreEqual(CollisionDetectionMode.ContinuousSpeculative, world.ObjectOf(Ball(BodyKind.Kinematic, true, 10f)).GetComponent<Rigidbody>().collisionDetectionMode);
            Assert.AreEqual(CollisionDetectionMode.Discrete, world.ObjectOf(Ball(BodyKind.Dynamic, false, 20f)).GetComponent<Rigidbody>().collisionDetectionMode);
        }

        [Test]
        public void CreatedBodiesAreRemovedAndUnknownHandlesThrow()
        {
            BodyHandle handle = world.CreateBody(new BodyDesc(BodyKind.Kinematic, BodyShape.Box(System.Numerics.Vector3.One), new System.Numerics.Vector3(1f, 2f, 3f), System.Numerics.Quaternion.Identity, 3f));

            Assert.IsTrue(world.Contains(handle));
            Assert.AreEqual(BodyKind.Kinematic, world.GetKind(handle));
            Assert.AreEqual(2f, world.GetBody(handle).Position.Y, 1e-5f);
            Assert.IsTrue(world.RemoveBody(handle));
            Assert.IsFalse(world.Contains(handle));
            Assert.IsFalse(world.RemoveBody(handle));
            Assert.Throws<ArgumentException>(() => world.GetBody(handle));
            Assert.IsFalse(new PhysicsBody(world, handle).IsValid);
        }

        [Test]
        public void TheSimulationModeIsOwnedByCountAndAnApplicationChangeIsKept()
        {
            Assert.AreEqual(SimulationMode.Script, Physics.simulationMode);

            PhysicsSimulationOwner.Acquire();
            PhysicsSimulationOwner.Release();

            Assert.AreEqual(SimulationMode.Script, Physics.simulationMode);

            PhysicsSimulationOwner.Acquire();
            Physics.simulationMode = SimulationMode.Update;
            PhysicsSimulationOwner.Release();

            Assert.AreEqual(SimulationMode.Update, Physics.simulationMode);
            Physics.simulationMode = SimulationMode.Script;
        }

        [Test]
        public void ANetworkManagerThatSimulatesPhysicsStepsTheSceneOfItsObjectsEachTick()
        {
            NetworkObject prefab = TestPrefabs.Create("Physical", PrefabId);
            var prefabBody = prefab.gameObject.AddComponent<Rigidbody>();
            prefabBody.useGravity = false;
            NetworkManager server = CreateManager(prefab, true);
            server.ServerManager.StartConnection(1);
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            server.ServerManager.Spawn(instance);
            PhysicsBody body = instance.Body;

            Assert.IsTrue(body.IsValid);
            body.Velocity = new System.Numerics.Vector3(3f, 0f, 0f);
            RunFrames(30);

            Assert.AreEqual(3f, body.Position.X, 0.2f);
            Assert.AreEqual(2, PhysicsSimulationOwner.Owners);
        }

        [Test]
        public void ScenesThatShareAPhysicsSceneAreOneWorldSteppedOnceAndSavedTogether()
        {
            Scene first = SceneManager.GetActiveScene();
            System.IO.File.WriteAllText(SharedScenePath, "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(SharedScenePath);
            Scene second = EditorSceneManager.OpenScene(SharedScenePath, OpenSceneMode.Additive);
            var worlds = new PhysicsWorlds();
            try
            {
                Rigidbody inFirst = RigidIn(first, new Vector3(100f, 0f, 0f));
                Rigidbody inSecond = RigidIn(second, new Vector3(200f, 0f, 0f));
                inSecond.linearVelocity = new Vector3(1f, 0f, 0f);
                UnityPhysicsWorld shared = worlds.Of(first);

                Assert.AreSame(shared, worlds.Of(second));
                Assert.AreEqual(2, shared.Scenes.Count);
                PhysicsSteps.Step(worlds, 0.5f);

                Assert.AreEqual(200.5f, inSecond.position.x, 1e-3f);
                PhysicsSnapshot snapshot = shared.CreateSnapshot();
                shared.Save(snapshot);
                inFirst.position = new Vector3(101f, 0f, 0f);
                inSecond.position = new Vector3(201f, 0f, 0f);
                shared.Load(snapshot);
                Assert.AreEqual(100f, inFirst.position.x, 1e-5f);
                Assert.AreEqual(200.5f, inSecond.position.x, 1e-3f);
                Assert.AreNotSame(shared, worlds.Of(scene));
            }
            finally
            {
                worlds.ReleaseStepping();
                EditorSceneManager.CloseScene(second, true);
                AssetDatabase.DeleteAsset(SharedScenePath);
            }
        }

        [Test]
        public void AWorldIsSteppedByOneOwnerAtATimeAndPassesOnWhenReleased()
        {
            var first = new PhysicsWorlds();
            var second = new PhysicsWorlds();
            Rigidbody moving = Rigid(new Vector3(0f, 0f, 0f), 1f);
            moving.linearVelocity = new Vector3(1f, 0f, 0f);
            first.Of(scene);
            second.Of(scene);
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
        public void AProxyIsKinematicFollowsItsTransformIsLeftOutOfTheSnapshotAndEnds()
        {
            NetworkObject networkObject = TestPrefabs.Create("Proxy", PrefabId);
            created.Add(networkObject.gameObject);
            SceneManager.MoveGameObjectToScene(networkObject.gameObject, scene);
            networkObject.gameObject.AddComponent<SphereCollider>().radius = 0.5f;
            var rigidbody = networkObject.gameObject.AddComponent<Rigidbody>();
            rigidbody.useGravity = false;
            var worlds = new PhysicsWorlds();
            networkObject.transform.position = new Vector3(3f, 0f, 0f);

            worlds.PlaceProxy(networkObject);

            Assert.IsTrue(rigidbody.isKinematic);
            Assert.AreEqual(3f, rigidbody.position.x, 1e-5f);
            PhysicsSnapshot snapshot = worlds.Of(scene).CreateSnapshot();
            worlds.Of(scene).Save(snapshot);
            Assert.AreEqual(0, UnityPhysicsWorld.EntryCount(snapshot));

            worlds.EndProxy(networkObject);

            Assert.IsFalse(rigidbody.isKinematic);
            worlds.Of(scene).Save(snapshot);
            Assert.AreEqual(1, UnityPhysicsWorld.EntryCount(snapshot));
        }

        [Test]
        public void AnObjectWithoutARigidbodyHasNoBody()
        {
            NetworkObject prefab = TestPrefabs.Create("Plain", PrefabId);
            NetworkManager server = CreateManager(prefab, false);
            server.ServerManager.StartConnection(1);
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            server.ServerManager.Spawn(instance);

            Assert.IsFalse(instance.Body.IsValid);
            Assert.IsFalse(UnityEngine.Object.Instantiate(prefab).Body.IsValid);
            Assert.AreEqual(1, PhysicsSimulationOwner.Owners);
        }

        private PhysicsBody Body(Vector3 position, float mass)
        {
            Rigidbody rigidbody = Rigid(position, mass);
            return new PhysicsBody(world, world.Register(rigidbody));
        }

        private BodyHandle Ball(BodyKind kind, bool continuous, float x)
        {
            var collider = new ColliderDesc(BodyShape.Sphere(0.5f), System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, ColliderMaterial.Default, 0, false);
            var motion = new Fomoxa.Networking.Simulation.BodyMotion(BodyLocks.None, false, 0f, 0f, continuous);
            return world.CreateBody(new BodyDesc(kind, new[] { collider }, new System.Numerics.Vector3(x, 50f, 0f), System.Numerics.Quaternion.Identity, 2f, motion));
        }

        private BodyHandle Cube(Vector3 position, int layer, bool trigger) =>
            world.CreateBody(new BodyDesc(BodyKind.Kinematic, new[] { CubeCollider(layer, trigger) }, position.ToNumerics(), System.Numerics.Quaternion.Identity, 1f));

        private static ColliderDesc CubeCollider(int layer, bool trigger) =>
            new ColliderDesc(BodyShape.Box(new System.Numerics.Vector3(0.5f, 0.5f, 0.5f)), System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, ColliderMaterial.Default, layer, trigger);

        private void AssertRay(in QueryFilter filter, BodyHandle body, float distance)
        {
            Assert.IsTrue(world.Raycast(System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitZ, 100f, filter, out RayHit hit));
            Assert.AreEqual(body, hit.Body);
            Assert.AreEqual(distance, hit.Distance, 1e-2f);
        }

        private Rigidbody Rigid(Vector3 position, float mass) => RigidIn(scene, position, mass);

        private Rigidbody RigidIn(Scene target, Vector3 position, float mass = 1f)
        {
            var gameObject = new GameObject("Body");
            created.Add(gameObject);
            SceneManager.MoveGameObjectToScene(gameObject, target);
            gameObject.transform.position = position;
            gameObject.AddComponent<SphereCollider>().radius = 0.5f;
            var rigidbody = gameObject.AddComponent<Rigidbody>();
            rigidbody.useGravity = false;
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
            serialized.FindProperty("physics").objectReferenceValue = TestPhysics.Rigidbody(manager.gameObject, simulate);
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
