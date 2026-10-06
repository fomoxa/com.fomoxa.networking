using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BundleFixture;
using Fomoxa.Networking.Sessions;
using Fomoxa.Unity.Tests.Support;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Fomoxa.Unity.Tests
{
    public sealed class OwnershipTest
    {
        private const uint PrefabId = 0xA1;
        private const ulong DoorId = 0x1234_5678_0000_0001;
        private const double FrameSeconds = 1.0 / 30;

        private readonly List<GameObject> created = new List<GameObject>();
        private readonly List<NetworkObject> serverScene = new List<NetworkObject>();
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
            server.ServerManager.FindSceneObjects = () => serverScene;
            client.ClientManager.FindSceneObjects = () => new List<NetworkObject>();
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
            serverScene.Clear();
            TestPrefabs.DestroyAllInScene();
            RecordingBehaviour.Clear();
        }

        [Test]
        public void ChangeOwnerAcceptsOnlyTheServerAndStartedPeers()
        {
            Connect();
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            NetworkObject unspawned = UnityEngine.Object.Instantiate(prefab);
            server.ServerManager.Spawn(instance);
            RunFrames(3);
            NetworkObject remote = (NetworkObject)client.ClientManager.Spawned[instance.ObjectId];

            Assert.Throws<ArgumentNullException>(() => server.ServerManager.ChangeOwner(null, 0));
            Assert.IsFalse(server.ServerManager.ChangeOwner(unspawned, clientPeerId));
            Assert.IsFalse(server.ServerManager.ChangeOwner(remote, clientPeerId));
            Assert.IsFalse(server.ServerManager.ChangeOwner(instance, clientPeerId + 1));
            Assert.AreEqual(0UL, instance.OwnerId);
            Assert.IsTrue(server.ServerManager.ChangeOwner(instance, clientPeerId));
            RunFrames(3);

            Assert.AreEqual(clientPeerId, instance.OwnerId);
            Assert.IsTrue(remote.IsOwner);
            Assert.AreEqual(new[] { "1 StartServer", "1 StartClient", "1 OwnerChangedServer 0", "1 OwnerChangedClient 0" }, RecordingBehaviour.Log);
        }

        [Test]
        public void SpawnWithAnOwnerThatIsNotAStartedPeerThrows()
        {
            Connect();
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);

            Assert.Throws<ArgumentException>(() => server.ServerManager.Spawn(instance, clientPeerId + 1));

            Assert.IsFalse(instance.IsSpawned);
            Assert.AreEqual(0, server.ServerManager.Spawned.Count);
        }

        [Test]
        public void LeavingPeerLosesItsObjects()
        {
            NetworkObject door = TestPrefabs.CreateSceneObject("Door", DoorId);
            serverScene.Add(door);
            var clientScene = new List<NetworkObject> { TestPrefabs.CreateSceneObject("ClientDoor", DoorId) };
            client.ClientManager.FindSceneObjects = () => clientScene;
            Connect();
            NetworkObject owned = UnityEngine.Object.Instantiate(prefab);
            NetworkObject serverOwned = UnityEngine.Object.Instantiate(prefab);
            server.ServerManager.Spawn(owned, clientPeerId);
            server.ServerManager.Spawn(serverOwned);
            Assert.IsTrue(server.ServerManager.ChangeOwner(door, clientPeerId));
            RunFrames(3);
            RecordingBehaviour.Log.Clear();

            client.ClientManager.StopConnection();
            RunFrames(3);

            Assert.IsTrue(owned == null);
            Assert.IsFalse(door == null);
            Assert.IsFalse(door.gameObject.activeSelf);
            Assert.IsFalse(door.IsSpawned);
            Assert.IsTrue(serverOwned.IsSpawned);
            Assert.AreEqual(1, server.ServerManager.Spawned.Count);
            Assert.AreEqual(new[] { "2 StopServer", "1 StopServer" }, RecordingBehaviour.Log.FindAll(entry => entry.EndsWith("StopServer")));
        }

        [Test]
        public void ObjectThatStaysGoesBackToTheServer()
        {
            Connect();
            NetworkObject owned = UnityEngine.Object.Instantiate(prefab);
            owned.DespawnWithOwner = false;
            server.ServerManager.Spawn(owned, clientPeerId);
            RunFrames(3);
            RecordingBehaviour.Log.Clear();

            client.ClientManager.StopConnection();
            RunFrames(3);

            Assert.IsTrue(owned.IsSpawned);
            Assert.AreEqual(0UL, owned.OwnerId);
            Assert.Contains($"1 OwnerChangedServer {clientPeerId}", RecordingBehaviour.Log);
        }

        [Test]
        public void StopServerExceptionOfALeavingOwnerIsLoggedAndTheOtherObjectsStillGo()
        {
            Connect();
            NetworkObject first = UnityEngine.Object.Instantiate(prefab);
            NetworkObject second = UnityEngine.Object.Instantiate(prefab);
            server.ServerManager.Spawn(first, clientPeerId);
            server.ServerManager.Spawn(second, clientPeerId);
            RunFrames(3);
            RecordingBehaviour.ThrowOn = "StopServer";

            LogAssert.Expect(LogType.Exception, new Regex("StopServer failed"));
            LogAssert.Expect(LogType.Exception, new Regex("StopServer failed"));
            client.ClientManager.StopConnection();
            RunFrames(3);

            Assert.IsTrue(first == null);
            Assert.IsTrue(second == null);
            Assert.AreEqual(ServerState.Started, server.ServerManager.State);
        }

        [Test]
        public void OwnerChangedServerExceptionOfALeavingOwnerIsLogged()
        {
            Connect();
            NetworkObject owned = UnityEngine.Object.Instantiate(prefab);
            owned.DespawnWithOwner = false;
            server.ServerManager.Spawn(owned, clientPeerId);
            RunFrames(3);
            RecordingBehaviour.ThrowOn = "OwnerChangedServer";

            LogAssert.Expect(LogType.Exception, new Regex("OwnerChangedServer failed"));
            client.ClientManager.StopConnection();
            RunFrames(3);

            Assert.AreEqual(0UL, owned.OwnerId);
            Assert.AreEqual(ServerState.Started, server.ServerManager.State);
        }

        [Test]
        public void HostClientStopDespawnsTheObjectsItOwns()
        {
            var hostNetwork = new GameObject("HostNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(hostNetwork.gameObject);
            NetworkManager host = CreateManager(hostNetwork);
            host.ServerManager.FindSceneObjects = () => new List<NetworkObject>();
            host.Prefabs.Register(prefab);
            host.ServerManager.StartConnection(2);
            host.ClientManager.StartConnection("unused.invalid", 2);
            RunFrames(20, host);
            NetworkObject player = UnityEngine.Object.Instantiate(prefab);
            host.ServerManager.Spawn(player, host.ClientManager.Objects.LocalPeerId);
            RunFrames(3, host);
            RecordingBehaviour.Log.Clear();

            host.ClientManager.StopConnection();
            RunFrames(3, host);

            Assert.IsTrue(player == null);
            Assert.AreEqual(new[] { "1 StopClient", "1 StopServer" }, RecordingBehaviour.Log);
            host.ServerManager.StopConnection();
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

        private void Connect()
        {
            server.ServerManager.StartConnection(1);
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(20);
            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
        }

        private void RunFrames(int count, params NetworkManager[] managers)
        {
            NetworkManager[] running = managers.Length == 0 ? new[] { server, client } : managers;
            for (int frame = 0; frame < count; frame++)
            {
                now += TimeSpan.FromSeconds(FrameSeconds);
                foreach (NetworkManager manager in running)
                {
                    manager.RunFrameStart(FrameSeconds, now);
                }

                foreach (NetworkManager manager in running)
                {
                    manager.RunFrameEnd();
                }
            }
        }
    }
}
