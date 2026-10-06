using System;
using System.Collections.Generic;
using System.Linq;
using BundleFixture;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;
using Fomoxa.Unity.Tests.Support;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Fomoxa.Unity.Tests
{
    public sealed class PredictionReconcileTest
    {
        private const uint PrefabId = 0xB8;
        private const uint LenientPrefabId = 0xB9;
        private const uint MovingPrefabId = 0xBA;
        private const uint EarlyPrefabId = 0xBB;
        private const uint TwicePrefabId = 0xBC;
        private const double FrameSeconds = 1.0 / 60;

        private readonly List<GameObject> created = new List<GameObject>();
        private readonly List<NetworkManager> managers = new List<NetworkManager>();
        private readonly List<NetworkObject> prefabs = new List<NetworkObject>();
        private InMemoryNetworkTransport network;
        private NetworkObject prefab;
        private NetworkObject lenientPrefab;
        private NetworkObject movingPrefab;
        private TimeSpan now;

        [SetUp]
        public void CreateNetwork()
        {
            now = TimeSpan.FromSeconds(1);
            network = new GameObject("InMemoryNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(network.gameObject);
            prefab = Prefab("Reconcile", PrefabId);
            prefab.gameObject.AddComponent<ReconcileBehaviour>();
            lenientPrefab = Prefab("Lenient", LenientPrefabId);
            lenientPrefab.gameObject.AddComponent<LenientReconcileBehaviour>();
            movingPrefab = Prefab("Moving", MovingPrefabId);
            movingPrefab.gameObject.AddComponent<NetworkTransform>().Visual = new GameObject("Visual").transform;
            movingPrefab.GetComponent<NetworkTransform>().Visual.SetParent(movingPrefab.transform, false);
            movingPrefab.GetComponent<NetworkTransform>().Visual.localPosition = new Vector3(0f, 1f, 0f);
            movingPrefab.gameObject.AddComponent<ReconcileBehaviour>().MoveTransform = true;
            Prefab("Early", EarlyPrefabId).gameObject.AddComponent<EarlyReconcileBehaviour>();
            Prefab("Twice", TwicePrefabId).gameObject.AddComponent<TwoReconcileBehaviour>();
        }

        [TearDown]
        public void DestroyManagers()
        {
            foreach (NetworkManager manager in managers)
            {
                manager.ClientManager.StopConnection();
                manager.ServerManager.StopConnection();
            }

            foreach (GameObject gameObject in created)
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }

            managers.Clear();
            created.Clear();
            prefabs.Clear();
            TestPrefabs.DestroyAllInScene();
        }

        [Test]
        public void AnAccuratePredictionOnlyReconcilesToStartAndMatchesTheServer()
        {
            NetworkManager server = StartServer();
            (NetworkManager client, ulong clientId) = Connect(server);
            ReconcileBehaviour onServer = Spawn<ReconcileBehaviour>(server, prefab, clientId);
            ReconcileBehaviour onClient = Remote<ReconcileBehaviour>(client, onServer);
            RunFrames(60);

            Assert.IsTrue(onClient.IsPredicting);
            Assert.AreEqual(1, onClient.ReconcileCount);
            Assert.AreEqual(1, onClient.ReconciledTicks.Count);
            Assert.IsFalse(onServer.IsPredicting);
            Assert.AreEqual(0, onServer.ReplayedApplies);
            AssertAgrees(onServer, onClient, onClient.ReconciledTicks[0]);
            Assert.Greater(onClient.PositionAt.Keys.Max(), onServer.PositionAt.Keys.Max());
        }

        [Test]
        public void AMispredictionIsRestoredFromTheServerAndReplayed()
        {
            NetworkManager server = StartServer();
            (NetworkManager client, ulong clientId) = Connect(server);
            ReconcileBehaviour onServer = Spawn<ReconcileBehaviour>(server, prefab, clientId);
            ReconcileBehaviour onClient = Remote<ReconcileBehaviour>(client, onServer);
            RunFrames(30);
            int replayedBefore = onClient.ReplayedApplies;

            onClient.Bias = 5;
            RunFrames(30);

            Assert.AreEqual(2, onClient.ReconcileCount);
            Assert.Greater(onClient.ReplayedApplies, replayedBefore);
            AssertAgrees(onServer, onClient, onClient.ReconciledTicks[1]);
        }

        [Test]
        public void AMatchesFunctionAcceptsASmallDifference()
        {
            NetworkManager server = StartServer();
            (NetworkManager client, ulong clientId) = Connect(server);
            LenientReconcileBehaviour onServer = Spawn<LenientReconcileBehaviour>(server, lenientPrefab, clientId);
            LenientReconcileBehaviour onClient = Remote<LenientReconcileBehaviour>(client, onServer);
            RunFrames(30);

            onClient.Bias = 5;
            RunFrames(30);

            Assert.AreEqual(1, onClient.ReconcileCount);
            Assert.AreEqual(onServer.PositionAt[onServer.PositionAt.Keys.Max()] + 5, onClient.PositionAt[onServer.PositionAt.Keys.Max()]);
        }

        [Test]
        public void TheHostAndAnObserverThatDoesNotOwnTheObjectDoNotPredict()
        {
            NetworkManager host = StartServer();
            host.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(20);
            (NetworkManager other, ulong _) = Connect(host);
            ReconcileBehaviour shared = Spawn<ReconcileBehaviour>(host, prefab, host.ClientManager.Objects.LocalPeerId);
            RunFrames(30);

            Assert.IsFalse(shared.IsPredicting);
            Assert.AreEqual(0, shared.ReconcileCount);
            Assert.AreEqual(0, shared.ReplayedApplies);
            Assert.IsNotEmpty(shared.PositionAt);
            ReconcileBehaviour onOther = Remote<ReconcileBehaviour>(other, shared);
            Assert.IsFalse(onOther.IsPredicting);
            Assert.IsEmpty(onOther.PositionAt);
        }

        [Test]
        public void AnOwnerChangeMovesThePredictionToTheNewOwner()
        {
            NetworkManager server = StartServer();
            (NetworkManager first, ulong firstId) = Connect(server);
            (NetworkManager second, ulong secondId) = Connect(server);
            ReconcileBehaviour onServer = Spawn<ReconcileBehaviour>(server, prefab, firstId);
            RunFrames(30);
            Assert.IsTrue(Remote<ReconcileBehaviour>(first, onServer).IsPredicting);

            Assert.IsTrue(server.ServerManager.ChangeOwner(onServer.NetworkObject, secondId));
            RunFrames(40);

            ReconcileBehaviour onFirst = Remote<ReconcileBehaviour>(first, onServer);
            ReconcileBehaviour onSecond = Remote<ReconcileBehaviour>(second, onServer);
            Assert.IsFalse(onFirst.IsPredicting);
            Assert.IsTrue(onSecond.IsPredicting);
            Assert.AreEqual(1, onSecond.ReconcileCount);
            AssertAgrees(onServer, onSecond, onSecond.ReconciledTicks[0]);
        }

        [Test]
        public void ThePredictedTransformLeadsTheServerAndTheVisualIsSmoothedAfterACorrection()
        {
            NetworkManager server = StartServer();
            (NetworkManager client, ulong clientId) = Connect(server);
            ReconcileBehaviour onServer = Spawn<ReconcileBehaviour>(server, movingPrefab, clientId);
            ReconcileBehaviour onClient = Remote<ReconcileBehaviour>(client, onServer);
            NetworkTransform clientTransform = onClient.GetComponent<NetworkTransform>();
            Transform visual = clientTransform.Visual;
            RunFrames(40);

            Assert.AreEqual(onClient.Position, onClient.transform.position.x, 1e-4f);
            Assert.Greater(onClient.transform.position.x, onServer.transform.position.x);
            Assert.AreEqual(0, clientTransform.SnapshotCount);

            Correct(onClient);

            float offset = visual.localPosition.x;
            Assert.Greater(offset, 0.5f);
            Assert.AreEqual(1f, visual.localPosition.y, 1e-4f);

            clientTransform.SmoothVisual(clientTransform.SmoothingTime / 2);

            Assert.AreEqual(offset / 2, visual.localPosition.x, 1e-3f);

            clientTransform.SmoothVisual(clientTransform.SmoothingTime);

            Assert.AreEqual(new Vector3(0f, 1f, 0f), visual.localPosition);

            visual.localPosition = new Vector3(0f, 2f, 0f);
            clientTransform.SmoothVisual(clientTransform.SmoothingTime);

            Assert.AreEqual(new Vector3(0f, 2f, 0f), visual.localPosition);

            Correct(onClient);

            Assert.Greater(visual.localPosition.x, 0.5f);
            Assert.AreEqual(2f, visual.localPosition.y, 1e-4f);

            clientTransform.SmoothVisual(clientTransform.SmoothingTime);

            Assert.AreEqual(new Vector3(0f, 2f, 0f), visual.localPosition);
        }

        [Test]
        public void AZeroSmoothingTimePutsTheVisualBackAtOnce()
        {
            NetworkManager server = StartServer();
            (NetworkManager client, ulong clientId) = Connect(server);
            ReconcileBehaviour onServer = Spawn<ReconcileBehaviour>(server, movingPrefab, clientId);
            ReconcileBehaviour onClient = Remote<ReconcileBehaviour>(client, onServer);
            NetworkTransform clientTransform = onClient.GetComponent<NetworkTransform>();
            clientTransform.SmoothingTime = 0f;
            RunFrames(40);

            Correct(onClient);

            Assert.AreEqual(new Vector3(0f, 1f, 0f), clientTransform.Visual.localPosition);
        }

        [Test]
        public void ReconcileBeforeUseAndASecondReconcileModelThrow()
        {
            NetworkManager server = StartServer();

            HandlerRegistrationException early = Assert.Throws<HandlerRegistrationException>(() => server.ServerManager.Spawn(UnityEngine.Object.Instantiate(prefabs.Single(candidate => candidate.PrefabId == EarlyPrefabId))));
            HandlerRegistrationException twice = Assert.Throws<HandlerRegistrationException>(() => server.ServerManager.Spawn(UnityEngine.Object.Instantiate(prefabs.Single(candidate => candidate.PrefabId == TwicePrefabId))));

            StringAssert.Contains("before its input model", early.Message);
            StringAssert.Contains("uses more than one reconcile model", twice.Message);
        }

        private void Correct(ReconcileBehaviour onClient)
        {
            onClient.Bias = 5;
            int reconciles = onClient.ReconcileCount;
            for (int frame = 0; frame < 30 && onClient.ReconcileCount == reconciles; frame++)
            {
                RunFrames(1);
            }

            Assert.AreEqual(reconciles + 1, onClient.ReconcileCount);
        }

        private static void AssertAgrees(ReconcileBehaviour onServer, ReconcileBehaviour onClient, uint after)
        {
            List<uint> common = onServer.PositionAt.Keys.Where(tick => tick > after && onClient.PositionAt.ContainsKey(tick)).ToList();
            Assert.IsNotEmpty(common);
            foreach (uint tick in common)
            {
                Assert.AreEqual(onServer.PositionAt[tick], onClient.PositionAt[tick], $"position at tick {tick}");
            }
        }

        private NetworkObject Prefab(string name, uint prefabId)
        {
            NetworkObject made = TestPrefabs.Create(name, prefabId);
            prefabs.Add(made);
            return made;
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

        private T Spawn<T>(NetworkManager server, NetworkObject source, ulong ownerId)
            where T : NetworkBehaviour
        {
            NetworkObject instance = UnityEngine.Object.Instantiate(source);
            server.ServerManager.Spawn(instance, ownerId);
            RunFrames(3);
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
            serialized.ApplyModifiedPropertiesWithoutUndo();
            manager.Registry = TestObjects.Registry();
            manager.Initialize();
            manager.FindServerSceneObjects = () => new List<NetworkObject>();
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
