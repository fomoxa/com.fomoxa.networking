using System;
using System.Collections.Generic;
using System.Linq;
using BundleFixture;
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
    public sealed class PredictionPhysicsTest
    {
        private const uint BallPrefabId = 0xBE;
        private const uint CratePrefabId = 0xBF;
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
        private TimeSpan now;

        [SetUp]
        public void CreateNetwork()
        {
            now = TimeSpan.FromSeconds(1);
            modeBefore = Physics.simulationMode;
            network = new GameObject("InMemoryNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(network.gameObject);
            serverScene = EditorSceneManager.NewPreviewScene();
            clientScene = EditorSceneManager.NewPreviewScene();
            ballPrefab = PhysicalPrefab("Ball", BallPrefabId);
            ballPrefab.gameObject.AddComponent<PhysicsReconcileBehaviour>();
            cratePrefab = PhysicalPrefab("Crate", CratePrefabId);
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
            Assert.AreEqual(0, PhysicsStepOwners.Count);
        }

        [Test]
        public void TheServerSendsTheStateAfterTheStepSoAnAccuratePredictionOnlyReconcilesToStart()
        {
            NetworkManager server = StartServer();
            (NetworkManager client, ulong clientId) = Connect(server);
            PhysicsReconcileBehaviour onServer = Spawn<PhysicsReconcileBehaviour>(server, client, ballPrefab, clientId);
            PhysicsReconcileBehaviour onClient = Remote<PhysicsReconcileBehaviour>(client, onServer);
            RunFrames(90);

            Assert.IsTrue(onClient.IsPredicting);
            Assert.AreEqual(1, onClient.ReconcileCount);
            Assert.AreEqual(0, onServer.ReplayedApplies);
            Assert.IsFalse(onClient.GetComponent<Rigidbody>().isKinematic);
            AssertAgrees(onServer, onClient, onClient.ReconciledTicks[0]);
            Assert.Greater(onClient.NetworkObject.Body.Position.X, 1f);
        }

        [Test]
        public void AMispredictionLoadsTheWorldSnapshotAndReplaysEveryBodyOfTheWorld()
        {
            NetworkManager server = StartServer();
            (NetworkManager client, ulong clientId) = Connect(server);
            PhysicsReconcileBehaviour onServer = Spawn<PhysicsReconcileBehaviour>(server, client, ballPrefab, clientId);
            PhysicsReconcileBehaviour onClient = Remote<PhysicsReconcileBehaviour>(client, onServer);
            Rigidbody cosmetic = Rigid(clientScene, new Vector3(0f, 20f, 0f));
            cosmetic.linearVelocity = new Vector3(0f, 0f, 1f);
            RunFrames(60);
            int ticks = 0;
            client.TimeManager.OnTick += () => ticks++;
            float cosmeticBefore = cosmetic.position.z;
            int replayedBefore = onClient.ReplayedApplies;

            onClient.Bias = 5f;
            for (int frame = 0; frame < 30 && onClient.ReconcileCount == 1; frame++)
            {
                RunFrames(1);
            }

            RunFrames(10);

            Assert.AreEqual(2, onClient.ReconcileCount);
            Assert.Greater(onClient.ReplayedApplies, replayedBefore);
            AssertAgrees(onServer, onClient, onClient.ReconciledTicks[1]);
            Assert.AreEqual(cosmeticBefore + ticks * (float)FrameSeconds, cosmetic.position.z, 1e-3f);
        }

        [Test]
        public void AnObjectTheClientDoesNotPredictIsAKinematicProxyLeftOutOfTheSnapshot()
        {
            NetworkManager server = StartServer();
            (NetworkManager client, ulong _) = Connect(server);
            NetworkTransform onServer = Spawn<NetworkTransform>(server, client, cratePrefab, 0);
            PhysicsBody crate = onServer.NetworkObject.Body;
            crate.Velocity = new System.Numerics.Vector3(2f, 0f, 0f);
            NetworkTransform onClient = Remote<NetworkTransform>(client, onServer);
            Rigidbody proxy = onClient.GetComponent<Rigidbody>();
            float start = onClient.transform.position.x;
            for (int frame = 0; frame < 30; frame++)
            {
                RunFrames(1);
                onClient.Advance(FrameSeconds);
            }

            Assert.IsTrue(proxy.isKinematic);
            Assert.IsFalse(onServer.GetComponent<Rigidbody>().isKinematic);
            Assert.Greater(onClient.transform.position.x, start + 0.5f);
            Assert.Less(onClient.transform.position.x, onServer.transform.position.x + 1e-3f);
            Assert.AreEqual(onClient.transform.position.x, proxy.position.x, 0.1f);
            UnityPhysicsWorld world = ((RigidbodyPhysics)client.Physics).Worlds.Of(clientScene);
            PhysicsSnapshot snapshot = world.CreateSnapshot();
            world.Save(snapshot);
            Assert.AreEqual(0, UnityPhysicsWorld.EntryCount(snapshot));

            client.ReleasePhysicsSimulation();

            Assert.IsFalse(proxy.isKinematic);
        }

        private static void AssertAgrees(PhysicsReconcileBehaviour onServer, PhysicsReconcileBehaviour onClient, uint after)
        {
            List<uint> common = onServer.PositionAt.Keys.Where(tick => tick > after && onClient.PositionAt.ContainsKey(tick)).ToList();
            Assert.IsNotEmpty(common);
            foreach (uint tick in common)
            {
                Assert.AreEqual(onServer.PositionAt[tick], onClient.PositionAt[tick], PhysicsReconcileBehaviour.Tolerance, $"position at tick {tick}");
            }
        }

        private NetworkObject PhysicalPrefab(string name, uint prefabId)
        {
            NetworkObject made = TestPrefabs.Create(name, prefabId);
            made.gameObject.AddComponent<SphereCollider>().radius = 0.5f;
            made.gameObject.AddComponent<Rigidbody>().useGravity = false;
            prefabs.Add(made);
            return made;
        }

        private Rigidbody Rigid(Scene scene, Vector3 position)
        {
            var gameObject = new GameObject("Cosmetic");
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

            SceneManager.MoveGameObjectToScene(((NetworkObject)client.ClientManager.Spawned[instance.ObjectId]).gameObject, clientScene);
            return instance.GetComponent<T>();
        }

        private static T Remote<T>(NetworkManager client, NetworkBehaviour onServer)
            where T : NetworkBehaviour =>
            ((NetworkObject)client.ClientManager.Spawned[onServer.NetworkObject.ObjectId]).GetComponent<T>();

        private NetworkManager CreateManager()
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("transport").objectReferenceValue = network;
            serialized.FindProperty("tickRate").intValue = 60;
            serialized.FindProperty("physics").objectReferenceValue = TestPhysics.Rigidbody(manager.gameObject, true);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            manager.Registry = TestObjects.Registry();
            manager.Initialize();
            manager.FindServerSceneObjects = () => new List<NetworkObject>();
            manager.FindClientSceneObjects = () => new List<NetworkObject>();
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
