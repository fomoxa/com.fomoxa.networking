using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BundleFixture;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Sessions;
using Fomoxa.Unity.Tests.Support;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Fomoxa.Unity.Tests
{
    public sealed class NetworkObjectTest
    {
        private const uint PrefabId = 0xA1;
        private const uint OtherPrefabId = 0xC3;
        private const double FrameSeconds = 1.0 / 30;
        private const string AssetFolder = "Assets/FomoxaNetworkObjectTest";

        private readonly List<GameObject> created = new List<GameObject>();
        private NetworkManager server;
        private NetworkManager client;
        private NetworkObject prefab;
        private ulong clientPeerId;
        private TimeSpan now;

        [SetUp]
        public void CreateManagers()
        {
            RecordingBehaviour.Clear();
            now = TimeSpan.Zero;
            var network = new GameObject("InMemoryNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(network.gameObject);
            server = CreateManager(network);
            client = CreateManager(network);
            prefab = TestPrefabs.Create("Prefab", PrefabId);
            server.Prefabs.Register(prefab);
            client.Prefabs.Register(prefab);
            server.ServerManager.OnRemoteConnectionState += args => clientPeerId = args.PeerId;
        }

        [TearDown]
        public void DestroyManagers()
        {
            foreach (NetworkManager manager in new[] { client, server })
            {
                manager.ClientManager.StopConnection();
                manager.ServerManager.StopConnection();
            }

            foreach (GameObject gameObject in created)
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }

            created.Clear();
            TestPrefabs.DestroyAllInScene();
            RecordingBehaviour.Clear();
        }

        [Test]
        public void SpawnGivesTheObjectAnIdAndStartsItOnTheServer()
        {
            Connect();
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);

            server.ServerManager.Spawn(instance);

            Assert.AreEqual(1u, instance.ObjectId);
            Assert.IsTrue(instance.IsSpawned);
            Assert.AreSame(instance, server.ServerManager.Spawned[1]);
            Assert.AreSame(instance, instance.Behaviours[0].NetworkObject);
            Assert.AreEqual(0, instance.Behaviours[0].BehaviourIndex);
            Assert.AreEqual(new[] { "1 StartServer" }, RecordingBehaviour.Log);
        }

        [Test]
        public void RemoteClientInstantiatesTheSpawnAtItsTransform()
        {
            Connect();
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab, new Vector3(1, 2, 3), Quaternion.Euler(0, 90, 0));
            instance.transform.localScale = new Vector3(2, 2, 2);

            server.ServerManager.Spawn(instance);
            RunFrames(3);

            NetworkObject remote = client.ClientManager.Spawned[instance.ObjectId];
            Assert.AreNotSame(instance, remote);
            Assert.AreEqual(PrefabId, remote.PrefabId);
            Assert.AreEqual(new Vector3(1, 2, 3), remote.transform.position);
            Assert.Less(Quaternion.Angle(Quaternion.Euler(0, 90, 0), remote.transform.rotation), 0.01f);
            Assert.AreEqual(new Vector3(2, 2, 2), remote.transform.localScale);
            Assert.IsNull(remote.transform.parent);
            Assert.AreSame(remote, remote.Behaviours[0].NetworkObject);
            Assert.AreEqual(new[] { "1 StartServer", "1 StartClient" }, RecordingBehaviour.Log);
        }

        [Test]
        public void LateClientReceivesTheCurrentTransform()
        {
            server.ServerManager.StartConnection(1);
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab, new Vector3(1, 2, 3), Quaternion.identity);
            server.ServerManager.Spawn(instance);
            instance.transform.position = new Vector3(4, 5, 6);

            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(20);

            Assert.AreEqual(new Vector3(4, 5, 6), client.ClientManager.Spawned[instance.ObjectId].transform.position);
        }

        [Test]
        public void OwnerIsReadFromTheTableOfEachSide()
        {
            Connect();
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);

            server.ServerManager.Spawn(instance, clientPeerId);
            RunFrames(3);

            NetworkObject remote = client.ClientManager.Spawned[instance.ObjectId];
            Assert.AreEqual(clientPeerId, instance.OwnerId);
            Assert.IsFalse(instance.IsOwner);
            Assert.AreEqual(clientPeerId, remote.OwnerId);
            Assert.IsTrue(remote.IsOwner);
        }

        [Test]
        public void OwnerChangeCallsTheCallbackOfEachSide()
        {
            Connect();
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            server.ServerManager.Spawn(instance);
            RunFrames(3);
            NetworkObject remote = client.ClientManager.Spawned[instance.ObjectId];
            RecordingBehaviour.Log.Clear();

            server.ServerManager.Objects.ChangeOwner(instance.ObjectId, clientPeerId);

            Assert.AreEqual(new[] { "1 OwnerChangedServer 0" }, RecordingBehaviour.Log);
            RunFrames(3);
            Assert.AreEqual(new[] { "1 OwnerChangedServer 0", "1 OwnerChangedClient 0" }, RecordingBehaviour.Log);
            Assert.IsTrue(remote.IsOwner);
        }

        [Test]
        public void DespawnStopsAndDestroysTheObjectOnBothSides()
        {
            Connect();
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            server.ServerManager.Spawn(instance);
            RunFrames(3);
            NetworkObject remote = client.ClientManager.Spawned[instance.ObjectId];
            RecordingBehaviour.Log.Clear();

            Assert.IsTrue(server.ServerManager.Despawn(instance));

            Assert.IsTrue(instance == null);
            Assert.AreEqual(0, server.ServerManager.Spawned.Count);
            Assert.AreEqual(new[] { "1 StopServer" }, RecordingBehaviour.Log);
            RunFrames(3);
            Assert.IsTrue(remote == null);
            Assert.AreEqual(0, client.ClientManager.Spawned.Count);
            Assert.AreEqual(new[] { "1 StopServer", "1 StopClient" }, RecordingBehaviour.Log);
            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
        }

        [Test]
        public void DespawnOfAnObjectThisServerDidNotSpawnReturnsFalseAndKeepsIt()
        {
            Connect();
            NetworkObject unspawned = UnityEngine.Object.Instantiate(prefab);
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            server.ServerManager.Spawn(instance);
            RunFrames(3);
            NetworkObject remote = client.ClientManager.Spawned[instance.ObjectId];

            Assert.IsFalse(server.ServerManager.Despawn(unspawned));
            Assert.IsFalse(server.ServerManager.Despawn(remote));

            Assert.IsFalse(unspawned == null);
            Assert.IsFalse(remote == null);
            Assert.AreEqual(1, server.ServerManager.Spawned.Count);
        }

        [Test]
        public void SpawnRejectsInvalidCalls()
        {
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            Assert.Throws<InvalidOperationException>(() => server.ServerManager.Spawn(instance));
            Connect();
            NetworkObject unregistered = TestPrefabs.Create("Unregistered", OtherPrefabId);
            server.ServerManager.Spawn(instance);

            Assert.Throws<ArgumentNullException>(() => server.ServerManager.Spawn(null));
            Assert.Throws<ArgumentException>(() => server.ServerManager.Spawn(unregistered));
            Assert.Throws<ArgumentException>(() => server.ServerManager.Spawn(instance));
            Assert.AreEqual(1, server.ServerManager.Spawned.Count);
        }

        [Test]
        public void SpawnOfAPrefabAssetThrows()
        {
            AssetDatabase.CreateFolder("Assets", "FomoxaNetworkObjectTest");
            try
            {
                NetworkObject source = TestPrefabs.Create("AssetPrefab", PrefabId);
                GameObject asset = PrefabUtility.SaveAsPrefabAsset(source.gameObject, AssetFolder + "/AssetPrefab.prefab");
                UnityEngine.Object.DestroyImmediate(source.gameObject);
                Connect();

                Assert.Throws<ArgumentException>(() => server.ServerManager.Spawn(asset.GetComponent<NetworkObject>()));
                Assert.AreEqual(0, server.ServerManager.Spawned.Count);
            }
            finally
            {
                AssetDatabase.DeleteAsset(AssetFolder);
            }
        }

        [Test]
        public void ServerStopDespawnsEveryObjectInReverseSpawnOrder()
        {
            Connect();
            NetworkObject first = UnityEngine.Object.Instantiate(prefab);
            NetworkObject second = UnityEngine.Object.Instantiate(prefab);
            server.ServerManager.Spawn(first);
            server.ServerManager.Spawn(second);
            RunFrames(3);
            RecordingBehaviour.Log.Clear();

            server.ServerManager.StopConnection();

            Assert.AreEqual(new[] { "2 StopServer", "1 StopServer" }, RecordingBehaviour.Log);
            Assert.IsTrue(first == null);
            Assert.IsTrue(second == null);
            Assert.AreEqual(0, server.ServerManager.Spawned.Count);
            RunFrames(5);
            Assert.AreEqual(0, client.ClientManager.Spawned.Count);
            Assert.AreEqual(new[] { "2 StopServer", "1 StopServer", "2 StopClient", "1 StopClient" }, RecordingBehaviour.Log);
            Assert.AreEqual(1, TestPrefabs.CountInScene());
        }

        [Test]
        public void ClientStopEndsItsInstancesInReverseSpawnOrder()
        {
            Connect();
            server.ServerManager.Spawn(UnityEngine.Object.Instantiate(prefab));
            server.ServerManager.Spawn(UnityEngine.Object.Instantiate(prefab));
            RunFrames(3);
            RecordingBehaviour.Log.Clear();

            client.ClientManager.StopConnection();

            Assert.AreEqual(new[] { "2 StopClient", "1 StopClient" }, RecordingBehaviour.Log);
            Assert.AreEqual(0, client.ClientManager.Spawned.Count);
            Assert.AreEqual(3, TestPrefabs.CountInScene());
        }

        [Test]
        public void UnknownPrefabStopsTheClientWithPrefabMismatch()
        {
            NetworkObject other = TestPrefabs.Create("Other", OtherPrefabId);
            server.Prefabs.Register(other);
            var mismatches = new List<ObjectMismatchArgs>();
            client.ClientManager.OnObjectMismatch += mismatches.Add;
            List<ConnectionStateArgs> states = RecordStates(client);
            Connect();

            server.ServerManager.Spawn(UnityEngine.Object.Instantiate(other));
            RunFrames(3);

            Assert.AreEqual(ObjectMismatchKind.UnknownPrefab, mismatches[0].Kind);
            Assert.AreEqual(OtherPrefabId, mismatches[0].PrefabId);
            Assert.AreEqual(StopReason.PrefabMismatch, states[states.Count - 1].Reason);
        }

        [Test]
        public void DifferentFingerprintStopsTheClientWithIncompatiblePrefab()
        {
            NetworkObject other = TestPrefabs.Create("Other", OtherPrefabId);
            server.Prefabs.Register(other);
            client.Prefabs.Register(OtherPrefabId, server.Prefabs.FingerprintOf(OtherPrefabId) + 1, (position, rotation) => null);
            var mismatches = new List<ObjectMismatchArgs>();
            client.ClientManager.OnObjectMismatch += mismatches.Add;
            Connect();

            server.ServerManager.Spawn(UnityEngine.Object.Instantiate(other));
            RunFrames(3);

            Assert.AreEqual(ObjectMismatchKind.IncompatiblePrefab, mismatches[0].Kind);
            Assert.AreEqual(ConnectionState.Stopped, client.ClientManager.State);
        }

        [Test]
        public void FactoryCreatesAndReleasesTheClientInstance()
        {
            NetworkObject other = TestPrefabs.Create("Other", OtherPrefabId);
            server.Prefabs.Register(other);
            NetworkObject pooled = UnityEngine.Object.Instantiate(other);
            pooled.gameObject.SetActive(false);
            Vector3 createdAt = Vector3.zero;
            NetworkObject released = null;
            client.Prefabs.Register(
                OtherPrefabId,
                server.Prefabs.FingerprintOf(OtherPrefabId),
                (position, rotation) =>
                {
                    createdAt = position;
                    pooled.transform.SetPositionAndRotation(position, rotation);
                    pooled.gameObject.SetActive(true);
                    return pooled;
                },
                instance =>
                {
                    released = instance;
                    instance.gameObject.SetActive(false);
                });
            Connect();
            NetworkObject spawned = UnityEngine.Object.Instantiate(other, new Vector3(7, 8, 9), Quaternion.identity);
            spawned.transform.localScale = new Vector3(3, 3, 3);

            server.ServerManager.Spawn(spawned);
            RunFrames(3);

            Assert.AreSame(pooled, client.ClientManager.Spawned[spawned.ObjectId]);
            Assert.AreEqual(new Vector3(7, 8, 9), createdAt);
            Assert.AreEqual(new Vector3(3, 3, 3), pooled.transform.localScale);
            Assert.AreEqual(new[] { "1 StartServer", "1 StartClient" }, RecordingBehaviour.Log);
            server.ServerManager.Despawn(spawned);
            RunFrames(3);
            Assert.AreSame(pooled, released);
            Assert.IsFalse(pooled == null);
            Assert.IsFalse(pooled.IsSpawned);
        }

        [Test]
        public void FactoryReturningNoInstanceIsAHandlerException()
        {
            NetworkObject other = TestPrefabs.Create("Other", OtherPrefabId);
            server.Prefabs.Register(other);
            client.Prefabs.Register(OtherPrefabId, server.Prefabs.FingerprintOf(OtherPrefabId), (position, rotation) => null);
            var errors = new List<HandlerExceptionArgs>();
            client.ClientManager.OnHandlerException += errors.Add;
            List<ConnectionStateArgs> states = RecordStates(client);
            Connect();

            server.ServerManager.Spawn(UnityEngine.Object.Instantiate(other));
            RunFrames(3);

            Assert.AreEqual(1, errors.Count);
            Assert.IsInstanceOf<InvalidOperationException>(errors[0].Exception);
            Assert.AreEqual(StopReason.HandlerException, states[states.Count - 1].Reason);
        }

        [Test]
        public void StartClientExceptionIsAHandlerExceptionAndDropsTheInstance()
        {
            RecordingBehaviour.ThrowOn = "StartClient";
            var errors = new List<HandlerExceptionArgs>();
            client.ClientManager.OnHandlerException += errors.Add;
            Connect();

            server.ServerManager.Spawn(UnityEngine.Object.Instantiate(prefab));
            RunFrames(3);

            Assert.AreEqual(1, errors.Count);
            Assert.AreEqual(ConnectionState.Stopped, client.ClientManager.State);
            Assert.AreEqual(0, client.ClientManager.Spawned.Count);
            Assert.AreEqual(2, TestPrefabs.CountInScene());
        }

        [Test]
        public void DestroyedClientInstanceIsForgottenWithAWarning()
        {
            Connect();
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            server.ServerManager.Spawn(instance);
            RunFrames(3);
            NetworkObject remote = client.ClientManager.Spawned[instance.ObjectId];

            LogAssert.Expect(LogType.Warning, new Regex("object 1 \\(prefab 0x000000A1\\) was destroyed on the client"));
            remote.HandleDestroy();
            UnityEngine.Object.DestroyImmediate(remote.gameObject);

            Assert.AreEqual(0, client.ClientManager.Spawned.Count);
            server.ServerManager.Despawn(instance);
            RunFrames(3);
            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
        }

        [Test]
        public void DestroyedServerInstanceIsDespawned()
        {
            Connect();
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            server.ServerManager.Spawn(instance);
            RunFrames(3);
            RecordingBehaviour.Log.Clear();

            instance.HandleDestroy();
            UnityEngine.Object.DestroyImmediate(instance.gameObject);

            Assert.AreEqual(0, server.ServerManager.Spawned.Count);
            Assert.AreEqual(new[] { "1 StopServer" }, RecordingBehaviour.Log);
            RunFrames(3);
            Assert.AreEqual(0, client.ClientManager.Spawned.Count);
            Assert.AreEqual(new[] { "1 StopServer", "1 StopClient" }, RecordingBehaviour.Log);
        }

        private NetworkManager CreateManager(NetworkTransport transport)
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("transport").objectReferenceValue = transport;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            manager.Registry = TestObjects.Registry();
            manager.Initialize();
            created.Add(manager.gameObject);
            return manager;
        }

        private static List<ConnectionStateArgs> RecordStates(NetworkManager manager)
        {
            var states = new List<ConnectionStateArgs>();
            manager.ClientManager.OnClientConnectionState += states.Add;
            return states;
        }

        private void Connect()
        {
            if (server.ServerManager.State != ServerState.Started)
            {
                server.ServerManager.StartConnection(1);
            }

            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(20);
            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
        }

        private void RunFrames(int count)
        {
            for (int frame = 0; frame < count; frame++)
            {
                now += TimeSpan.FromSeconds(FrameSeconds);
                foreach (NetworkManager manager in new[] { server, client })
                {
                    manager.RunFrameStart(FrameSeconds, now);
                }

                foreach (NetworkManager manager in new[] { server, client })
                {
                    manager.RunFrameEnd();
                }
            }
        }
    }
}
