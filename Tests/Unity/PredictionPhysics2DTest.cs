using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BundleFixture;
using Fomoxa.Networking;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Simulation;
using Fomoxa.Unity.Tests.Support;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity.Tests
{
    public sealed class PredictionPhysics2DTest
    {
        private const uint BallPrefabId = 0xC4;
        private const uint CratePrefabId = 0xC5;
        private const double FrameSeconds = 1.0 / 60;

        private readonly List<GameObject> created = new List<GameObject>();
        private readonly List<NetworkManager> managers = new List<NetworkManager>();
        private readonly List<NetworkObject> prefabs = new List<NetworkObject>();
        private InMemoryNetworkTransport network;
        private NetworkObject ballPrefab;
        private NetworkObject cratePrefab;
        private Scene serverScene;
        private Scene clientScene;
        private SimulationMode modeBefore;
        private SimulationMode2D mode2DBefore;
        private TimeSpan now;

        [SetUp]
        public void CreateNetwork()
        {
            now = TimeSpan.FromSeconds(1);
            modeBefore = Physics.simulationMode;
            mode2DBefore = Physics2D.simulationMode;
            network = new GameObject("InMemoryNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(network.gameObject);
            serverScene = EditorSceneManager.NewPreviewScene();
            clientScene = EditorSceneManager.NewPreviewScene();
            ballPrefab = PhysicalPrefab("Ball2D", BallPrefabId);
            ballPrefab.gameObject.AddComponent<PhysicsReconcileBehaviour2D>();
            cratePrefab = PhysicalPrefab("Crate2D", CratePrefabId);
            cratePrefab.gameObject.AddComponent<NetworkTransform>();
        }

        [TearDown]
        public void DestroyManagers()
        {
            foreach (NetworkManager manager in managers)
            {
                manager.ClientManager.StopConnection();
                manager.ServerManager.StopConnection();
                manager.ReleasePhysicsSimulation();
            }

            foreach (GameObject gameObject in created)
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }

            managers.Clear();
            created.Clear();
            prefabs.Clear();
            TestPrefabs.DestroyAllInScene();
            EditorSceneManager.ClosePreviewScene(serverScene);
            EditorSceneManager.ClosePreviewScene(clientScene);
            Assert.AreEqual(modeBefore, Physics.simulationMode);
            Assert.AreEqual(mode2DBefore, Physics2D.simulationMode);
            Assert.AreEqual(0, PhysicsStepOwners.Count);
            Assert.AreEqual(0, ContactTrackers.Count);
        }

        [Test]
        public void TheServerSendsTheStateAfterTheStepSoAnAccurate2DPredictionOnlyReconcilesToStart()
        {
            Assert.AreNotEqual(serverScene.GetPhysicsScene2D(), clientScene.GetPhysicsScene2D());
            NetworkManager server = StartServer();
            (NetworkManager client, ulong clientId) = Connect(server);
            PhysicsReconcileBehaviour2D onServer = Spawn<PhysicsReconcileBehaviour2D>(server, client, ballPrefab, clientId);
            PhysicsReconcileBehaviour2D onClient = Remote<PhysicsReconcileBehaviour2D>(client, onServer);
            RunFrames(90);

            Assert.IsTrue(onClient.IsPredicting);
            Assert.AreEqual(1, onClient.ReconcileCount);
            Assert.AreEqual(0, onServer.ReplayedApplies);
            Assert.AreEqual(RigidbodyType2D.Dynamic, onClient.GetComponent<Rigidbody2D>().bodyType);
            AssertAgrees(onServer, onClient, onClient.ReconciledTicks[0]);
            Assert.Greater(onClient.NetworkObject.Body2D.Position.X, 1f);
        }

        [Test]
        public void AMispredictionLoadsBothWorldsOfTheSceneAndReplaysThemTogether()
        {
            NetworkManager server = StartServer();
            (NetworkManager client, ulong clientId) = Connect(server);
            PhysicsReconcileBehaviour2D onServer = Spawn<PhysicsReconcileBehaviour2D>(server, client, ballPrefab, clientId);
            PhysicsReconcileBehaviour2D onClient = Remote<PhysicsReconcileBehaviour2D>(client, onServer);
            Rigidbody2D cosmetic2D = Rigid2D(clientScene, new Vector2(0f, 20f));
            cosmetic2D.linearVelocity = new Vector2(0f, 1f);
            Rigidbody cosmetic3D = Rigid3D(clientScene, new Vector3(0f, 20f, 0f));
            cosmetic3D.linearVelocity = new Vector3(0f, 0f, 1f);
            RunFrames(60);
            int ticks = 0;
            client.TimeManager.OnTick += () => ticks++;
            float cosmetic2DBefore = cosmetic2D.position.y;
            float cosmetic3DBefore = cosmetic3D.position.z;
            int replayedBefore = onClient.ReplayedApplies;

            onClient.Bias = 5f;
            RunUntilSecondReconcile(onClient);

            Assert.AreEqual(2, onClient.ReconcileCount);
            Assert.Greater(onClient.ReplayedApplies, replayedBefore);
            AssertAgrees(onServer, onClient, onClient.ReconciledTicks[1]);
            Assert.AreEqual(cosmetic2DBefore + ticks * (float)FrameSeconds, cosmetic2D.position.y, 1e-3f);
            Assert.AreEqual(cosmetic3DBefore + ticks * (float)FrameSeconds, cosmetic3D.position.z, 1e-3f);
        }

        [Test]
        public void AGroupMissingASnapshotAtTheTargetLoadsNoWorldSoItsWorldsStayAtOneTick()
        {
            NetworkManager server = StartServer();
            (NetworkManager client, ulong clientId) = Connect(server);
            PhysicsReconcileBehaviour2D onServer = Spawn<PhysicsReconcileBehaviour2D>(server, client, ballPrefab, clientId);
            PhysicsReconcileBehaviour2D onClient = Remote<PhysicsReconcileBehaviour2D>(client, onServer);
            Rigidbody2D cosmetic2D = Rigid2D(clientScene, new Vector2(0f, 20f));
            cosmetic2D.linearVelocity = new Vector2(0f, 1f);
            Rigidbody cosmetic3D = Rigid3D(clientScene, new Vector3(0f, 20f, 0f));
            cosmetic3D.linearVelocity = new Vector3(0f, 0f, 1f);
            RunFrames(60);
            int ticks = 0;
            client.TimeManager.OnTick += () => ticks++;
            float cosmetic2DBefore = cosmetic2D.position.y;
            float cosmetic3DBefore = cosmetic3D.position.z;

            onClient.Bias = 5f;
            PhysicsWorlds physics = client.ClientManager.Physics;
            PhysicsHistory history2D = physics.HistoryOf(physics.Of2D(clientScene), client.ClientManager.InputRules.History);
            for (int frame = 0; frame < 30 && onClient.ReconcileCount == 1; frame++)
            {
                RunFrames(1);
                history2D.Clear();
            }

            float moved2D = cosmetic2D.position.y - cosmetic2DBefore;
            float moved3D = cosmetic3D.position.z - cosmetic3DBefore;
            Assert.AreEqual(2, onClient.ReconcileCount);
            Assert.AreEqual(moved2D, moved3D, 1e-3f);
            Assert.Greater(moved3D, (ticks + 0.5f) * (float)FrameSeconds);
        }

        [Test]
        public void AReplayWithoutALoadKeepsTheFirstRunSnapshotsOfTheWorlds()
        {
            NetworkManager server = StartServer();
            (NetworkManager client, ulong clientId) = Connect(server);
            PhysicsReconcileBehaviour2D onServer = Spawn<PhysicsReconcileBehaviour2D>(server, client, ballPrefab, clientId);
            PhysicsReconcileBehaviour2D onClient = Remote<PhysicsReconcileBehaviour2D>(client, onServer);
            Rigidbody cosmetic3D = Rigid3D(clientScene, new Vector3(0f, 20f, 0f));
            cosmetic3D.linearVelocity = new Vector3(0f, 0f, 1f);
            RunFrames(60);
            var firstRun = new Dictionary<uint, float>();
            client.TimeManager.OnTick += () => firstRun[client.TimeManager.PredictionTick] = cosmetic3D.position.z;

            onClient.Bias = 5f;
            PhysicsWorlds physics = client.ClientManager.Physics;
            PhysicsHistory history2D = physics.HistoryOf(physics.Of2D(clientScene), client.ClientManager.InputRules.History);
            for (int frame = 0; frame < 30 && onClient.ReconcileCount == 1; frame++)
            {
                RunFrames(1);
                history2D.Clear();
            }

            uint replayed = onClient.ReconciledTicks[1] + 1;
            PhysicsHistory history3D = physics.HistoryOf(physics.Of(clientScene), client.ClientManager.InputRules.History);
            Assert.AreEqual(2, onClient.ReconcileCount);
            Assert.IsTrue(firstRun.ContainsKey(replayed));
            Assert.IsTrue(history3D.Load(replayed));
            Assert.AreEqual(firstRun[replayed], cosmetic3D.position.z, 1e-4f);
        }

        [Test]
        public void ReplaysThatAgreeOnAContactDoNotRepeatItsEnter()
        {
            NetworkManager server = StartServer();
            (NetworkManager client, ulong clientId) = Connect(server);
            PhysicsReconcileBehaviour2D onServer = Spawn<PhysicsReconcileBehaviour2D>(server, client, ballPrefab, clientId);
            PhysicsReconcileBehaviour2D onClient = Remote<PhysicsReconcileBehaviour2D>(client, onServer);
            NetworkTrigger2D zone = Zone2D(clientScene, new Vector2(101f, 0f), new Vector2(198f, 4f));
            var events = new List<string>();
            zone.OnEnter += other => events.Add("enter");
            zone.OnExit += other => events.Add("exit");
            try
            {
                RunFrames(90);
                int replayedBefore = onClient.ReplayedApplies;

                for (int round = 0; round < 3; round++)
                {
                    int reconciled = onClient.ReconcileCount;
                    onClient.Bias = 0.01f;
                    for (int frame = 0; frame < 30 && onClient.ReconcileCount == reconciled; frame++)
                    {
                        RunFrames(1);
                    }
                }

                RunFrames(10);

                Assert.Greater(onClient.ReplayedApplies, replayedBefore);
                CollectionAssert.AreEqual(new[] { "enter" }, events);
                Assert.IsTrue(zone.Touching.Contains(onClient.GetComponent<Collider2D>()));
                Assert.AreEqual(0, onClient.ReplayFlagMismatches);
                Assert.IsFalse(client.ClientManager.IsReplaying);
                Assert.IsFalse(onServer.IsReplaying);
            }
            finally
            {
                Disable(zone);
            }
        }

        [Test]
        public void AMispredictedContactIsUndoneByOneExitAtTheEndOfTheReplay()
        {
            NetworkManager server = StartServer();
            (NetworkManager client, ulong clientId) = Connect(server);
            PhysicsReconcileBehaviour2D onServer = Spawn<PhysicsReconcileBehaviour2D>(server, client, ballPrefab, clientId);
            PhysicsReconcileBehaviour2D onClient = Remote<PhysicsReconcileBehaviour2D>(client, onServer);
            RunFrames(60);
            float edge = onClient.NetworkObject.Body2D.Position.X - 0.6f;
            NetworkTrigger2D zone = Zone2D(clientScene, new Vector2(edge - 2.2f, 0f), new Vector2(4.4f, 4f));
            var events = new List<string>();
            zone.OnEnter += other => events.Add(client.ClientManager.IsReplaying ? "enter while replaying" : "enter");
            zone.OnExit += other => events.Add(client.ClientManager.IsReplaying ? "exit while replaying" : "exit");
            try
            {
                onClient.Bias = -20f;
                RunUntilSecondReconcile(onClient);

                Assert.AreEqual(2, onClient.ReconcileCount);
                CollectionAssert.AreEqual(new[] { "enter", "exit" }, events);
                Assert.AreEqual(0, zone.Touching.Count);
                Assert.Greater(onClient.NetworkObject.Body2D.Position.X, edge + 0.5f);
            }
            finally
            {
                Disable(zone);
            }
        }

        [Test]
        public void AReplayWithoutALoadKeepsTheFirstRunContactsOfTheTracker()
        {
            NetworkManager server = StartServer();
            (NetworkManager client, ulong clientId) = Connect(server);
            PhysicsReconcileBehaviour2D onServer = Spawn<PhysicsReconcileBehaviour2D>(server, client, ballPrefab, clientId);
            PhysicsReconcileBehaviour2D onClient = Remote<PhysicsReconcileBehaviour2D>(client, onServer);
            RunFrames(60);
            float edge = onClient.NetworkObject.Body2D.Position.X - 0.6f;
            NetworkTrigger2D zone = Zone2D(clientScene, new Vector2(edge - 2.2f, 0f), new Vector2(4.4f, 4f));
            var events = new List<string>();
            zone.OnEnter += other => events.Add("enter");
            zone.OnExit += other => events.Add("exit");
            PhysicsWorlds physics = client.ClientManager.Physics;
            PhysicsHistory history2D = physics.HistoryOf(physics.Of2D(clientScene), client.ClientManager.InputRules.History);
            try
            {
                onClient.Bias = -20f;
                for (int frame = 0; frame < 30 && onClient.ReconcileCount == 1; frame++)
                {
                    RunFrames(1);
                    history2D.Clear();
                }

                CollectionAssert.AreEqual(new[] { "enter", "exit" }, events);
                ContactTracker<Collider2D> tracker = ContactTrackers.Of(clientScene.GetPhysicsScene2D());
                tracker.Restore(onClient.ReconciledTicks[1] + 1);
                tracker.Publish();

                CollectionAssert.AreEqual(new[] { "enter", "exit", "enter" }, events);
            }
            finally
            {
                Disable(zone);
            }
        }

        [Test]
        public void AnObjectTheClientDoesNotPredictIsAKinematic2DProxyLeftOutOfTheSnapshot()
        {
            NetworkManager server = StartServer();
            (NetworkManager client, ulong _) = Connect(server);
            NetworkTransform onServer = Spawn<NetworkTransform>(server, client, cratePrefab, 0);
            PhysicsBody2D crate = onServer.NetworkObject.Body2D;
            crate.Velocity = new System.Numerics.Vector2(2f, 0f);
            NetworkTransform onClient = Remote<NetworkTransform>(client, onServer);
            Rigidbody2D proxy = onClient.GetComponent<Rigidbody2D>();
            RunFrames(30);
            onClient.transform.position = new Vector3(1.5f, 0f, 0f);
            RunFrames(1);

            Assert.Greater(onServer.GetComponent<Rigidbody2D>().position.x, 0.3f);
            Assert.AreEqual(RigidbodyType2D.Kinematic, proxy.bodyType);
            Assert.AreEqual(RigidbodyType2D.Dynamic, onServer.GetComponent<Rigidbody2D>().bodyType);
            Assert.AreEqual(1.5f, proxy.position.x, 1e-4f);
            Assert.AreEqual(1.5f, onClient.transform.position.x, 1e-4f);
            UnityPhysicsWorld2D world = client.ClientManager.Physics.Of2D(clientScene);
            PhysicsSnapshot snapshot = world.CreateSnapshot();
            world.Save(snapshot);
            Assert.AreEqual(0, UnityPhysicsWorld2D.EntryCount(snapshot));

            client.ReleasePhysicsSimulation();

            Assert.AreEqual(RigidbodyType2D.Dynamic, proxy.bodyType);
        }

        [Test]
        public void ScenesThatShareAWorldOfEitherKindReplayAsOneGroup()
        {
            Scene third = EditorSceneManager.NewPreviewScene();
            try
            {
                var worlds = new PhysicsWorlds(PhysicsBackend.Rigidbody);
                var groups = new ReplayGroups();
                groups.Add(worlds.Of(serverScene), worlds.Of2D(serverScene));
                groups.Add(worlds.Of(clientScene), worlds.Of2D(clientScene));
                groups.Add(worlds.Of(third), worlds.Of2D(third));

                Assert.AreEqual(3, groups.Groups.Count);

                groups.Add(worlds.Of(serverScene), worlds.Of2D(clientScene));

                Assert.AreEqual(2, groups.Groups.Count);
                ReplayGroup joined = groups.Groups.Single(group => group.Worlds.Contains(worlds.Of(serverScene)));
                Assert.AreEqual(2, joined.Worlds.Count);
                Assert.AreEqual(2, joined.Worlds2D.Count);
                Assert.IsTrue(joined.Worlds.Contains(worlds.Of(clientScene)));
                Assert.IsFalse(joined.Worlds2D.Contains(worlds.Of2D(third)));

                groups.Clear();
                groups.Add(worlds.Of(third), worlds.Of2D(third));

                Assert.AreEqual(1, groups.Groups.Count);
                Assert.AreEqual(1, groups.Groups[0].Worlds.Count);
                Assert.AreEqual(1, groups.Groups[0].Worlds2D.Count);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(third);
            }
        }

        private static void AssertAgrees(PhysicsReconcileBehaviour2D onServer, PhysicsReconcileBehaviour2D onClient, uint after)
        {
            List<uint> common = onServer.PositionAt.Keys.Where(tick => tick > after && onClient.PositionAt.ContainsKey(tick)).ToList();
            Assert.IsNotEmpty(common);
            foreach (uint tick in common)
            {
                Assert.AreEqual(onServer.PositionAt[tick], onClient.PositionAt[tick], PhysicsReconcileBehaviour2D.Tolerance, $"position at tick {tick}");
            }
        }

        private void RunUntilSecondReconcile(PhysicsReconcileBehaviour2D onClient)
        {
            for (int frame = 0; frame < 30 && onClient.ReconcileCount == 1; frame++)
            {
                RunFrames(1);
            }

            RunFrames(10);
        }

        private NetworkTrigger2D Zone2D(Scene scene, Vector2 position, Vector2 size)
        {
            var zone = new GameObject("Zone2D");
            created.Add(zone);
            SceneManager.MoveGameObjectToScene(zone, scene);
            zone.transform.position = position;
            var box = zone.AddComponent<BoxCollider2D>();
            box.size = size;
            box.isTrigger = true;
            var trigger = zone.AddComponent<NetworkTrigger2D>();
            typeof(NetworkTrigger2D).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(trigger, null);
            return trigger;
        }

        private static void Disable(NetworkTrigger2D trigger) =>
            typeof(NetworkTrigger2D).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(trigger, null);

        private NetworkObject PhysicalPrefab(string name, uint prefabId)
        {
            NetworkObject made = TestPrefabs.Create(name, prefabId);
            made.gameObject.AddComponent<CircleCollider2D>().radius = 0.5f;
            made.gameObject.AddComponent<Rigidbody2D>().gravityScale = 0f;
            prefabs.Add(made);
            return made;
        }

        private Rigidbody2D Rigid2D(Scene scene, Vector2 position)
        {
            var gameObject = new GameObject("Cosmetic2D");
            created.Add(gameObject);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            gameObject.transform.position = position;
            gameObject.AddComponent<CircleCollider2D>().radius = 0.5f;
            var rigidbody = gameObject.AddComponent<Rigidbody2D>();
            rigidbody.gravityScale = 0f;
            rigidbody.position = position;
            return rigidbody;
        }

        private Rigidbody Rigid3D(Scene scene, Vector3 position)
        {
            var gameObject = new GameObject("Cosmetic3D");
            created.Add(gameObject);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            gameObject.transform.position = position;
            gameObject.AddComponent<SphereCollider>().radius = 0.5f;
            var rigidbody = gameObject.AddComponent<Rigidbody>();
            rigidbody.useGravity = false;
            rigidbody.position = position;
            return rigidbody;
        }

        private NetworkManager StartServer()
        {
            NetworkManager server = CreateManager();
            server.ServerManager.StartConnection(1);
            return server;
        }

        private (NetworkManager Manager, ulong PeerId) Connect(NetworkManager server)
        {
            ulong peerId = 0;
            Action<ConnectionStateArgs> record = args => peerId = args.PeerId;
            server.ServerManager.OnRemoteConnectionState += record;
            NetworkManager client = CreateManager();
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(30);
            server.ServerManager.OnRemoteConnectionState -= record;
            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
            return (client, peerId);
        }

        private T Spawn<T>(NetworkManager server, NetworkManager client, NetworkObject source, ulong ownerId)
            where T : NetworkBehaviour
        {
            NetworkObject instance = UnityEngine.Object.Instantiate(source);
            SceneManager.MoveGameObjectToScene(instance.gameObject, serverScene);
            server.ServerManager.Spawn(instance, ownerId);
            for (int frame = 0; frame < 10 && !client.ClientManager.Spawned.ContainsKey(instance.ObjectId); frame++)
            {
                RunFrames(1);
            }

            SceneManager.MoveGameObjectToScene(client.ClientManager.Spawned[instance.ObjectId].gameObject, clientScene);
            return instance.GetComponent<T>();
        }

        private static T Remote<T>(NetworkManager client, NetworkBehaviour onServer)
            where T : NetworkBehaviour =>
            client.ClientManager.Spawned[onServer.NetworkObject.ObjectId].GetComponent<T>();

        private NetworkManager CreateManager()
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("transport").objectReferenceValue = network;
            serialized.FindProperty("tickRate").intValue = 60;
            serialized.FindProperty("simulatePhysics").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            manager.Registry = TestObjects.Registry();
            manager.Initialize();
            manager.ServerManager.FindSceneObjects = () => new List<NetworkObject>();
            manager.ClientManager.FindSceneObjects = () => new List<NetworkObject>();
            foreach (NetworkObject registered in prefabs)
            {
                manager.Prefabs.Register(registered);
            }

            created.Add(manager.gameObject);
            managers.Add(manager);
            return manager;
        }

        private void RunFrames(int count)
        {
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
