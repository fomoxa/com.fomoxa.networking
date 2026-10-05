using System;
using System.Collections.Generic;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Networking.Sessions;
using Fomoxa.Unity.Tests.Support;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Fomoxa.Unity.Tests
{
    public sealed class DistanceRuleTest
    {
        private const uint PrefabId = 0xB2;
        private const double FrameSeconds = 1.0 / 30;

        private readonly List<GameObject> created = new List<GameObject>();
        private readonly List<NetworkManager> managers = new List<NetworkManager>();
        private InMemoryNetworkTransport network;
        private NetworkManager server;
        private NetworkObject prefab;
        private DistanceRule rule;
        private TimeSpan now;

        [SetUp]
        public void CreateServer()
        {
            RecordingBehaviour.Clear();
            now = TimeSpan.Zero;
            network = new GameObject("InMemoryNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(network.gameObject);
            rule = ScriptableObject.CreateInstance<DistanceRule>();
            rule.Radius = 10;
            prefab = TestPrefabs.Create("Prefab", PrefabId);
            server = CreateManager(network);
            server.ServerManager.StartConnection(1);
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

            UnityEngine.Object.DestroyImmediate(rule);
            managers.Clear();
            created.Clear();
            TestPrefabs.DestroyAllInScene();
            RecordingBehaviour.Clear();
        }

        [Test]
        public void APeerObservesTheObjectsWithinTheRadiusOfItsAnchor()
        {
            (NetworkManager client, ulong peerId) = Connect();
            Spawn(Vector3.zero, peerId);
            NetworkObject near = Spawn(new Vector3(10, 0, 0));
            NetworkObject far = Spawn(new Vector3(10.5f, 0, 0));
            RunFrames(3);

            Assert.IsTrue(client.ClientManager.Spawned.ContainsKey(near.ObjectId));
            Assert.IsFalse(client.ClientManager.Spawned.ContainsKey(far.ObjectId));
            Assert.IsTrue(rule.Observes(near, peerId));
            Assert.IsFalse(rule.Observes(far, peerId));
        }

        [Test]
        public void ObserverRangeReplacesTheRadiusForItsObject()
        {
            (NetworkManager client, ulong peerId) = Connect();
            Spawn(Vector3.zero, peerId);
            NetworkObject wide = UnityEngine.Object.Instantiate(prefab, new Vector3(30, 0, 0), Quaternion.identity);
            wide.gameObject.AddComponent<ObserverRange>().Radius = 40;
            server.ServerManager.Spawn(wide);
            NetworkObject narrow = UnityEngine.Object.Instantiate(prefab, new Vector3(5, 0, 0), Quaternion.identity);
            narrow.gameObject.AddComponent<ObserverRange>().Radius = 2;
            server.ServerManager.Spawn(narrow);
            RunFrames(3);

            Assert.IsTrue(client.ClientManager.Spawned.ContainsKey(wide.ObjectId));
            Assert.IsFalse(client.ClientManager.Spawned.ContainsKey(narrow.ObjectId));
        }

        [Test]
        public void APeerWithoutAnAnchorSeesOnlyEveryoneObjects()
        {
            (NetworkManager client, ulong _) = Connect();
            NetworkObject plain = Spawn(Vector3.zero);
            NetworkObject everyone = UnityEngine.Object.Instantiate(prefab, new Vector3(500, 0, 0), Quaternion.identity);
            everyone.Visibility = NetworkVisibility.Everyone;
            server.ServerManager.Spawn(everyone);
            RunFrames(3);

            Assert.IsFalse(client.ClientManager.Spawned.ContainsKey(plain.ObjectId));
            Assert.IsTrue(client.ClientManager.Spawned.ContainsKey(everyone.ObjectId));
        }

        [Test]
        public void TheFirstAnchorShowsTheObjectsAroundItWithoutWaitingForTheRound()
        {
            server.ServerManager.ObserverInterval = 1000;
            (NetworkManager client, ulong peerId) = Connect();
            NetworkObject near = Spawn(new Vector3(3, 0, 0));
            NetworkObject alsoNear = Spawn(new Vector3(-3, 0, 0));
            RunFrames(3);
            Assert.AreEqual(0, client.ClientManager.Spawned.Count);

            Spawn(Vector3.zero, peerId);
            RunFrames(3);

            Assert.IsTrue(client.ClientManager.Spawned.ContainsKey(near.ObjectId));
            Assert.IsTrue(client.ClientManager.Spawned.ContainsKey(alsoNear.ObjectId));
        }

        [Test]
        public void AnObjectHandedToAPeerWithoutAnchorsBecomesItsFirstAnchor()
        {
            server.ServerManager.ObserverInterval = 1000;
            (NetworkManager client, ulong peerId) = Connect();
            NetworkObject anchor = Spawn(new Vector3(100, 0, 0));
            NetworkObject near = Spawn(new Vector3(104, 0, 0));
            RunFrames(3);

            Assert.IsTrue(server.ServerManager.ChangeOwner(anchor, peerId));
            RunFrames(3);

            Assert.IsTrue(client.ClientManager.Spawned.ContainsKey(anchor.ObjectId));
            Assert.IsTrue(client.ClientManager.Spawned.ContainsKey(near.ObjectId));
        }

        [Test]
        public void ASecondAnchorWaitsForTheRound()
        {
            server.ServerManager.ObserverInterval = 1000;
            (NetworkManager client, ulong peerId) = Connect();
            Spawn(Vector3.zero, peerId);
            NetworkObject second = Spawn(new Vector3(100, 0, 0));
            NetworkObject nearSecond = Spawn(new Vector3(104, 0, 0));
            RunFrames(3);

            Assert.IsTrue(server.ServerManager.ChangeOwner(second, peerId));
            RunFrames(3);

            Assert.IsTrue(client.ClientManager.Spawned.ContainsKey(second.ObjectId));
            Assert.IsFalse(client.ClientManager.Spawned.ContainsKey(nearSecond.ObjectId));
            server.ServerManager.RebuildObserversOfPeer(peerId);
            RunFrames(3);
            Assert.IsTrue(client.ClientManager.Spawned.ContainsKey(nearSecond.ObjectId));
        }

        [Test]
        public void ObservesOnAnObjectThatIsNotSpawnedIsFalse()
        {
            (NetworkManager _, ulong peerId) = Connect();
            Spawn(Vector3.zero, peerId);
            NetworkObject unspawned = UnityEngine.Object.Instantiate(prefab, Vector3.one, Quaternion.identity);

            Assert.IsFalse(rule.Observes(unspawned, peerId));
            Assert.IsFalse(rule.Observes(null, peerId));
        }

        [Test]
        public void AMovingAnchorChangesTheSetWithinTheInterval()
        {
            server.ServerManager.ObserverInterval = 2;
            (NetworkManager client, ulong peerId) = Connect();
            NetworkObject anchor = Spawn(Vector3.zero, peerId);
            NetworkObject near = Spawn(new Vector3(5, 0, 0));
            RunFrames(3);
            Assert.IsTrue(client.ClientManager.Spawned.ContainsKey(near.ObjectId));

            anchor.transform.position = new Vector3(100, 0, 0);
            RunFrames(3);
            Assert.IsFalse(client.ClientManager.Spawned.ContainsKey(near.ObjectId));

            anchor.transform.position = new Vector3(6, 0, 0);
            RunFrames(3);
            Assert.IsTrue(client.ClientManager.Spawned.ContainsKey(near.ObjectId));
        }

        [Test]
        public void ARebuildReadsTheAnchorWhereItIsNow()
        {
            server.ServerManager.ObserverInterval = 1000;
            (NetworkManager _, ulong peerId) = Connect();
            NetworkObject anchor = Spawn(Vector3.zero, peerId);
            NetworkObject near = Spawn(new Vector3(5, 0, 0));
            Assert.IsTrue(server.ServerManager.IsObserver(near, peerId));

            anchor.transform.position = new Vector3(100, 0, 0);
            server.ServerManager.RebuildObservers(near);

            Assert.IsFalse(server.ServerManager.IsObserver(near, peerId));
        }

        [Test]
        public void EveryAnchorOfAPeerCounts()
        {
            (NetworkManager client, ulong peerId) = Connect();
            Spawn(Vector3.zero, peerId);
            Spawn(new Vector3(200, 0, 0), peerId);
            NetworkObject nearFirst = Spawn(new Vector3(5, 0, 0));
            NetworkObject nearSecond = Spawn(new Vector3(205, 0, 0));
            NetworkObject between = Spawn(new Vector3(100, 0, 0));
            RunFrames(3);

            Assert.IsTrue(client.ClientManager.Spawned.ContainsKey(nearFirst.ObjectId));
            Assert.IsTrue(client.ClientManager.Spawned.ContainsKey(nearSecond.ObjectId));
            Assert.IsFalse(client.ClientManager.Spawned.ContainsKey(between.ObjectId));
        }

        [Test]
        public void ARadiusBelowZeroBecomesZero()
        {
            rule.Radius = -1;
            ObserverRange range = new GameObject("Range").AddComponent<ObserverRange>();
            created.Add(range.gameObject);
            range.Radius = -5;

            Assert.AreEqual(0f, rule.Radius);
            Assert.AreEqual(0f, range.Radius);
        }

        private NetworkObject Spawn(Vector3 position, ulong ownerId = 0)
        {
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
            server.ServerManager.Spawn(instance, ownerId);
            return instance;
        }

        private (NetworkManager Manager, ulong PeerId) Connect()
        {
            ulong peerId = 0;
            Action<ConnectionStateArgs> record = args => peerId = args.PeerId;
            server.ServerManager.OnRemoteConnectionState += record;
            NetworkManager client = CreateManager(network);
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(20);
            server.ServerManager.OnRemoteConnectionState -= record;
            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
            return (client, peerId);
        }

        private NetworkManager CreateManager(NetworkTransport transport)
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("transport").objectReferenceValue = transport;
            serialized.FindProperty("observerRule").objectReferenceValue = rule;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            manager.Registry = TestObjects.Registry();
            manager.Initialize();
            manager.ServerManager.FindSceneObjects = () => new List<NetworkObject>();
            manager.ClientManager.FindSceneObjects = () => new List<NetworkObject>();
            manager.Prefabs.Register(prefab);
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
