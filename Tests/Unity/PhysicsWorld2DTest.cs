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
        public void QueriesFindRegisteredBodiesAndOverlapTakesTriggers()
        {
            BodyHandle handle = world.Register(Rigid(new Vector2(5f, 0f), 1f));
            Rigid(new Vector2(5f, 5f), 1f);
            Rigidbody2D triggerBody = Rigid(new Vector2(5f, 0.2f), 1f);
            triggerBody.GetComponent<Collider2D>().isTrigger = true;
            BodyHandle trigger = world.Register(triggerBody);
            world.Step(0f);

            Assert.IsTrue(world.Raycast(System.Numerics.Vector2.Zero, new System.Numerics.Vector2(1f, 0f), 100f, out RayHit2D hit));
            Assert.AreEqual(handle, hit.Body);
            Assert.AreEqual(4.5f, hit.Distance, 1e-3f);
            var results = new BodyHandle[4];
            Assert.AreEqual(2, world.Overlap(new System.Numerics.Vector2(5f, 0f), 1f, results));
            CollectionAssert.AreEquivalent(new[] { handle, trigger }, new[] { results[0], results[1] });
            Assert.AreEqual(1, world.Overlap(BodyShape2D.Circle(1f), new System.Numerics.Vector2(5f, 0f), 0f, new QueryFilter(-1, default, false), results));
            Assert.AreEqual(handle, results[0]);
            Assert.IsTrue(world.Raycast(new System.Numerics.Vector2(0f, 5f), new System.Numerics.Vector2(1f, 0f), 100f, out RayHit2D unregistered));
            Assert.IsFalse(unregistered.Body.IsValid);
            Assert.IsFalse(world.Raycast(System.Numerics.Vector2.Zero, new System.Numerics.Vector2(-1f, 0f), 100f, out _));
        }

        [Test]
        public void FiltersChooseLayersAndTriggersAndSkipABody()
        {
            BodyHandle trigger = Square(new Vector2(2f, 0f), 0, true);
            BodyHandle near = Square(new Vector2(5f, 0f), 0, false);
            BodyHandle far = Square(new Vector2(8f, 0f), 3, false);
            world.Step(0f);
            var solid = new QueryFilter(-1, default, false);
            var results = new BodyHandle[4];

            AssertRay(QueryFilter.All, trigger, 1.5f);
            AssertRay(solid, near, 4.5f);
            AssertRay(new QueryFilter(-1, near, false), far, 7.5f);
            AssertRay(new QueryFilter(1 << 3, default, true), far, 7.5f);
            Assert.IsFalse(world.Raycast(System.Numerics.Vector2.Zero, System.Numerics.Vector2.UnitX, 100f, new QueryFilter(1 << 4, default, true), out _));
            BodyShape2D row = BodyShape2D.Box(new System.Numerics.Vector2(5f, 1f));
            var middle = new System.Numerics.Vector2(5f, 0f);
            Assert.AreEqual(3, world.Overlap(row, middle, 0f, QueryFilter.All, results));
            Assert.AreEqual(2, world.Overlap(row, middle, 0f, solid, results));
            Assert.AreEqual(1, world.Overlap(row, middle, 0f, new QueryFilter(1, near, true), results));
            Assert.AreEqual(trigger, results[0]);
            Assert.AreEqual(3, world.Overlap(System.Numerics.Vector2.Zero, 10f, results));
        }

        [Test]
        public void ShapeCastsAndOverlapsTakeABoxACircleAndACapsule()
        {
            BodyHandle target = Square(new Vector2(5f, 0f), 0, false);
            world.Step(0f);
            var shapes = new[]
            {
                (BodyShape2D.Box(new System.Numerics.Vector2(0.5f, 0.5f)), 0f, 4f),
                (BodyShape2D.Circle(0.5f), 0f, 4f),
                (BodyShape2D.Capsule(0.5f, 0.5f), MathF.PI / 2f, 3.5f),
            };
            var results = new BodyHandle[4];

            foreach ((BodyShape2D shape, float rotation, float distance) in shapes)
            {
                Assert.IsTrue(world.ShapeCast(shape, System.Numerics.Vector2.Zero, rotation, System.Numerics.Vector2.UnitX, 100f, QueryFilter.All, out RayHit2D hit), shape.Kind.ToString());
                Assert.AreEqual(target, hit.Body);
                Assert.AreEqual(distance, hit.Distance, 2e-2f, shape.Kind.ToString());
                Assert.AreEqual(-1f, hit.Normal.X, 1e-3f);
                Assert.AreEqual(1, world.Overlap(shape, new System.Numerics.Vector2(4.2f, 0f), rotation, QueryFilter.All, results), shape.Kind.ToString());
                Assert.AreEqual(0, world.Overlap(shape, new System.Numerics.Vector2(4.2f, 2f), rotation, QueryFilter.All, results), shape.Kind.ToString());
            }

            BodyShape2D polygon = BodyShape2D.ConvexPolygon(new[] { System.Numerics.Vector2.Zero, System.Numerics.Vector2.UnitX, System.Numerics.Vector2.UnitY });
            Assert.Throws<ArgumentException>(() => world.Overlap(polygon, System.Numerics.Vector2.Zero, 0f, QueryFilter.All, results));
            Assert.Throws<ArgumentException>(() => world.ShapeCast(polygon, System.Numerics.Vector2.Zero, 0f, System.Numerics.Vector2.UnitX, 1f, QueryFilter.All, out _));
        }

        [Test]
        public void AQueryStartingInsideAColliderHitsItAtDistanceZero()
        {
            BodyHandle inside = Square(Vector2.zero, 0, false);
            BodyHandle beyond = Square(new Vector2(5f, 0f), 0, false);
            world.Step(0f);
            var origin = new System.Numerics.Vector2(0.1f, 0f);

            Assert.IsTrue(world.Raycast(origin, System.Numerics.Vector2.UnitX, 100f, QueryFilter.All, out RayHit2D ray));
            Assert.IsTrue(world.ShapeCast(BodyShape2D.Circle(0.2f), origin, 0f, System.Numerics.Vector2.UnitX, 100f, QueryFilter.All, out RayHit2D cast));
            Assert.IsTrue(world.Raycast(origin, System.Numerics.Vector2.UnitX, 100f, new QueryFilter(-1, inside, true), out RayHit2D skipped));

            Assert.AreEqual((inside, 0f), (ray.Body, ray.Distance));
            Assert.AreEqual((inside, 0f), (cast.Body, cast.Distance));
            Assert.AreEqual(beyond, skipped.Body);
            Assert.AreEqual(4.4f, skipped.Distance, 1e-3f);
        }

        [Test]
        public void QueriesSeeABodySetThisTickAndAfterLoad()
        {
            BodyHandle body = world.CreateBody(new BodyDesc2D(BodyKind.Dynamic, new[] { SquareCollider(0, false) }, new System.Numerics.Vector2(5f, 0f), 0f, 1f, new BodyMotion2D(BodyLocks2D.None, 0f, 0f, 0f)));
            world.Step(0f);
            PhysicsSnapshot snapshot = world.CreateSnapshot();
            world.Save(snapshot);

            world.SetBody(body, new BodyState2D { Position = new System.Numerics.Vector2(10f, 0f) });
            AssertRay(QueryFilter.All, body, 9.5f);
            world.Load(snapshot);
            AssertRay(QueryFilter.All, body, 4.5f);
        }

        [Test]
        public void ImpulsesChangeTheVelocityAtOnceAndForcesAndTorquesActAtTheNextStep()
        {
            BodyHandle spun = Disc(BodyKind.Dynamic, false, 0f);
            BodyHandle struck = Disc(BodyKind.Dynamic, false, 10f);
            BodyHandle twisted = Disc(BodyKind.Dynamic, false, 20f);
            BodyHandle pushed = Disc(BodyKind.Dynamic, false, 30f);
            BodyHandle carried = Disc(BodyKind.Kinematic, false, 40f);
            world.Step(0f);
            float inertia = world.ObjectOf(spun).GetComponent<Rigidbody2D>().inertia;

            world.AddTorqueImpulse(spun, 0.25f);
            world.AddImpulseAtPoint(struck, new System.Numerics.Vector2(0f, 1f), world.GetBody(struck).Position + new System.Numerics.Vector2(0.5f, 0f));
            world.AddTorque(twisted, 0.5f);
            world.AddForceAtPoint(pushed, new System.Numerics.Vector2(0f, 1f), world.GetBody(pushed).Position + new System.Numerics.Vector2(0.5f, 0f));
            world.AddTorqueImpulse(carried, 0.25f);

            Assert.AreEqual(0.25f / inertia, world.GetBody(spun).AngularVelocity, 1e-4f);
            Assert.AreEqual(0.5f, world.GetBody(struck).Velocity.Y, 1e-5f);
            Assert.AreEqual(0.5f / inertia, world.GetBody(struck).AngularVelocity, 1e-4f);
            Assert.AreEqual(0f, world.GetBody(twisted).AngularVelocity);
            Assert.AreEqual(System.Numerics.Vector2.Zero, world.GetBody(pushed).Velocity);
            Assert.AreEqual(0f, world.GetBody(carried).AngularVelocity);

            world.Step(0.5f);

            Assert.AreEqual(0.25f / inertia, world.GetBody(twisted).AngularVelocity, 1e-3f);
            Assert.AreEqual(0.25f, world.GetBody(pushed).Velocity.Y, 1e-4f);
            Assert.AreEqual(0.25f / inertia, world.GetBody(pushed).AngularVelocity, 1e-3f);
        }

        [Test]
        public void ContinuousCollisionChoosesTheDetectionModeOfACreatedBody()
        {
            Assert.AreEqual(CollisionDetectionMode2D.Continuous, world.ObjectOf(Disc(BodyKind.Dynamic, true, 0f)).GetComponent<Rigidbody2D>().collisionDetectionMode);
            Assert.AreEqual(CollisionDetectionMode2D.Discrete, world.ObjectOf(Disc(BodyKind.Dynamic, false, 20f)).GetComponent<Rigidbody2D>().collisionDetectionMode);
        }

        [Test]
        public void ContactPointsPointFromTheOtherSideAndCarryTheOverlap()
        {
            var ground = new GameObject("Ground");
            created.Add(ground);
            SceneManager.MoveGameObjectToScene(ground, scene);
            ground.layer = 1;
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.AddComponent<BoxCollider2D>().size = new Vector2(10f, 1f);
            Rigidbody2D lowerBody = Rigid(new Vector2(0f, 0.5f), 1f);
            Rigidbody2D upperBody = Rigid(new Vector2(0f, 1.5f), 1f);
            lowerBody.gravityScale = 1f;
            upperBody.gravityScale = 1f;
            BodyHandle lower = world.Register(lowerBody);
            BodyHandle upper = world.Register(upperBody);
            BodyHandle away = world.Register(Rigid(new Vector2(5f, 3f), 1f));
            for (int step = 0; step < 60; step++)
            {
                world.Step(1f / 30f);
            }

            var points = new List<ContactPointInfo2D> { default, default };

            Assert.Greater(world.GetContactPoints(lower, default, QueryFilter.All, points), 0);
            AssertPoints(points, System.Numerics.Vector2.UnitY, 0f);
            Assert.Greater(world.GetContactPoints(upper, lower, points), 0);
            AssertPoints(points, System.Numerics.Vector2.UnitY, 1f);
            Assert.Greater(new PhysicsBody2D(world, lower).GetContactPoints(upper, points), 0);
            AssertPoints(points, -System.Numerics.Vector2.UnitY, 1f);
            Assert.Greater(world.GetContactPoints(lower, default, new QueryFilter(1 << 1, default, false), points), 0);
            Assert.AreEqual(0, world.GetContactPoints(lower, default, new QueryFilter(1 << 0, default, true), points));
            Assert.AreEqual(0, world.GetContactPoints(lower, upper, new QueryFilter(-1, upper, true), points));
            Assert.AreEqual(0, world.GetContactPoints(upper, default, QueryFilter.All, points));
            Assert.AreEqual(0, world.GetContactPoints(away, default, QueryFilter.All, points));
            Assert.AreEqual(0, world.GetContactPoints(default, default, QueryFilter.All, points));
            Assert.AreEqual(0, points.Count);
        }

        [Test]
        public void BetweenLoadAndTheNextStepContactPointsAreThoseOfTheLoadedTick()
        {
            var ground = new GameObject("Ground");
            created.Add(ground);
            SceneManager.MoveGameObjectToScene(ground, scene);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.AddComponent<BoxCollider2D>().size = new Vector2(10f, 1f);
            Rigidbody2D ballBody = Rigid(new Vector2(0f, 0.5f), 1f);
            ballBody.gravityScale = 1f;
            ballBody.sleepMode = RigidbodySleepMode2D.NeverSleep;
            BodyHandle ball = world.Register(ballBody);
            for (int step = 0; step < 60; step++)
            {
                world.Step(1f / 30f);
            }

            PhysicsSnapshot resting = world.CreateSnapshot();
            world.Save(resting);
            world.SetBody(ball, new BodyState2D { Position = new System.Numerics.Vector2(0f, 5f) });
            world.Step(1f / 30f);
            PhysicsSnapshot falling = world.CreateSnapshot();
            world.Save(falling);
            var points = new List<ContactPointInfo2D>();

            Assert.AreEqual(0, world.GetContactPoints(ball, default, points));

            world.Load(resting);

            Assert.Greater(world.GetContactPoints(ball, default, points), 0);
            AssertPoints(points, System.Numerics.Vector2.UnitY, 0f);
            Assert.AreEqual(0, world.GetContactPoints(ball, default, new QueryFilter(1 << 3, default, true), points));

            world.Step(1f / 30f);

            Assert.Greater(world.GetContactPoints(ball, default, points), 0);
            AssertPoints(points, System.Numerics.Vector2.UnitY, 0f);

            world.Load(falling);

            Assert.AreEqual(0, world.GetContactPoints(ball, default, points));

            world.Step(1f / 30f);

            Assert.AreEqual(0, world.GetContactPoints(ball, default, points));
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
            var worlds = new PhysicsWorlds();
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
            var first = new PhysicsWorlds();
            var second = new PhysicsWorlds();
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
            var worlds = new PhysicsWorlds();
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

        private BodyHandle Disc(BodyKind kind, bool continuous, float x)
        {
            var collider = new ColliderDesc2D(BodyShape2D.Circle(0.5f), System.Numerics.Vector2.Zero, 0f, ColliderMaterial.Default, 0, false);
            var motion = new BodyMotion2D(BodyLocks2D.None, 0f, 0f, 0f, continuous);
            return world.CreateBody(new BodyDesc2D(kind, new[] { collider }, new System.Numerics.Vector2(x, 50f), 0f, 2f, motion));
        }

        private BodyHandle Square(Vector2 position, int layer, bool trigger) =>
            world.CreateBody(new BodyDesc2D(BodyKind.Kinematic, new[] { SquareCollider(layer, trigger) }, position.ToNumerics(), 0f, 1f));

        private static ColliderDesc2D SquareCollider(int layer, bool trigger) =>
            new ColliderDesc2D(BodyShape2D.Box(new System.Numerics.Vector2(0.5f, 0.5f)), System.Numerics.Vector2.Zero, 0f, ColliderMaterial.Default, layer, trigger);

        private void AssertRay(in QueryFilter filter, BodyHandle body, float distance)
        {
            Assert.IsTrue(world.Raycast(System.Numerics.Vector2.Zero, System.Numerics.Vector2.UnitX, 100f, filter, out RayHit2D hit));
            Assert.AreEqual(body, hit.Body);
            Assert.AreEqual(distance, hit.Distance, 1e-2f);
        }

        private static void AssertPoints(List<ContactPointInfo2D> points, System.Numerics.Vector2 normal, float height)
        {
            foreach (ContactPointInfo2D point in points)
            {
                Assert.AreEqual(normal.X, point.Normal.X, 1e-3f);
                Assert.AreEqual(normal.Y, point.Normal.Y, 1e-3f);
                Assert.AreEqual(height, point.Point.Y, 0.02f);
                Assert.AreEqual(0f, point.Depth, 0.02f);
            }
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
