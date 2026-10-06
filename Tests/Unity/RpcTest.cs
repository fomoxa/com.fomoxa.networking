using System.Collections.Generic;
using System.Text.RegularExpressions;
using System;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking;
using Fomoxa.Unity.Tests.Support;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;
using UnityEngine;

namespace Fomoxa.Unity.Tests
{
    public sealed class RpcTest
    {
        private const uint PrefabId = 0xA1;
        private const uint ConflictingPrefabId = 0xA2;
        private const uint UndeclaredPrefabId = 0xA3;
        private const double FrameSeconds = 1.0 / 30;

        private readonly List<GameObject> created = new List<GameObject>();
        private readonly List<NetworkManager> managers = new List<NetworkManager>();
        private InMemoryNetworkTransport network;
        private NetworkManager server;
        private NetworkObject prefab;
        private TimeSpan now;

        [SetUp]
        public void CreateServer()
        {
            RecordingBehaviour.Clear();
            now = TimeSpan.Zero;
            network = new GameObject("InMemoryNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(network.gameObject);
            prefab = TestPrefabs.Create("Prefab", PrefabId);
            prefab.gameObject.AddComponent<RpcBehaviour>();
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

            managers.Clear();
            created.Clear();
            TestPrefabs.DestroyAllInScene();
            RecordingBehaviour.Clear();
        }

        [Test]
        public void ServerRpcFromTheOwnerRunsWithItsPeerId()
        {
            (NetworkManager owner, ulong ownerId) = Connect();
            RpcBehaviour onServer = SpawnOwnedBy(ownerId);

            Assert.AreEqual(SendResult.Queued, Remote(owner, onServer).Fire(7));
            RunFrames(3);

            Assert.AreEqual(new[] { $"Fire {ownerId} 7" }, onServer.Calls);
        }

        [Test]
        public void ServerRpcFromAnotherPeerIsDroppedWithAWarningUnlessOwnershipIsOff()
        {
            (NetworkManager owner, ulong ownerId) = Connect();
            (NetworkManager other, ulong otherId) = Connect();
            RpcBehaviour onServer = SpawnOwnedBy(ownerId);

            LogAssert.Expect(LogType.Warning, new Regex($"dropped server RPC 0x20000011 to object 1 from peer {otherId}, which does not own"));
            Remote(other, onServer).Fire(8);
            Remote(other, onServer).Open(9);
            RunFrames(3);

            Assert.AreEqual(new[] { $"Open {otherId} 9" }, onServer.Calls);
            Assert.AreEqual(ConnectionState.Started, other.ClientManager.State);
        }

        [Test]
        public void ObserversRpcReachesEveryClientAndTargetRpcOnlyOne()
        {
            (NetworkManager first, ulong firstId) = Connect();
            (NetworkManager second, ulong _) = Connect();
            RpcBehaviour onServer = SpawnOwnedBy(0);

            Assert.AreEqual(2, onServer.Announce(5));
            Assert.AreEqual(SendResult.Queued, onServer.Whisper(firstId, 6));
            RunFrames(3);

            Assert.AreEqual(new[] { "Announce 5", "Whisper 6" }, Remote(first, onServer).Calls);
            Assert.AreEqual(new[] { "Announce 5" }, Remote(second, onServer).Calls);
            Assert.IsEmpty(onServer.Calls);
        }

        [Test]
        public void SendingFromASideWhereTheObjectIsNotSpawnedThrows()
        {
            (NetworkManager client, ulong _) = Connect();
            RpcBehaviour unspawned = UnityEngine.Object.Instantiate(prefab).GetComponent<RpcBehaviour>();
            RpcBehaviour onServer = SpawnOwnedBy(0);
            RpcBehaviour onClient = Remote(client, onServer);

            Assert.Throws<InvalidOperationException>(() => unspawned.Fire(1));
            Assert.Throws<InvalidOperationException>(() => unspawned.Announce(1));
            Assert.Throws<InvalidOperationException>(() => onServer.Fire(1));
            Assert.Throws<InvalidOperationException>(() => onClient.Announce(1));
            Assert.Throws<InvalidOperationException>(() => onClient.Whisper(1, 1));
        }

        [Test]
        public void SendingANullModelThrowsAndSendsNothing()
        {
            (NetworkManager owner, ulong ownerId) = Connect();
            RpcBehaviour onServer = SpawnOwnedBy(ownerId);
            RpcBehaviour onOwner = Remote(owner, onServer);

            Assert.Throws<ArgumentNullException>(() => onOwner.Fire(null));
            Assert.Throws<ArgumentNullException>(() => onServer.Announce(null));
            Assert.Throws<ArgumentNullException>(() => onServer.Whisper(ownerId, null));
            RunFrames(3);

            Assert.IsEmpty(onServer.Calls);
            Assert.IsEmpty(onOwner.Calls);
            Assert.AreEqual(ConnectionState.Started, owner.ClientManager.State);
        }

        [Test]
        public void RpcIdNotInTheSchemaCannotBeRouted()
        {
            NetworkObject undeclared = TestPrefabs.Create("Undeclared", UndeclaredPrefabId);
            undeclared.gameObject.AddComponent<UndeclaredRpcBehaviour>();
            server.Prefabs.Register(undeclared);
            NetworkObject instance = UnityEngine.Object.Instantiate(undeclared);

            HandlerRegistrationException error = Assert.Throws<HandlerRegistrationException>(() => server.ServerManager.Spawn(instance));

            StringAssert.Contains("0x20000015 is not declared in the schema", error.Message);
            Assert.IsFalse(instance.IsSpawned);
        }

        [Test]
        public void TwoBehaviourTypesCannotShareAnRpcId()
        {
            SpawnOwnedBy(0);
            NetworkObject conflicting = TestPrefabs.Create("Conflicting", ConflictingPrefabId);
            conflicting.gameObject.AddComponent<ConflictingRpcBehaviour>();
            server.Prefabs.Register(conflicting);
            NetworkObject instance = UnityEngine.Object.Instantiate(conflicting);

            Assert.Throws<HandlerRegistrationException>(() => server.ServerManager.Spawn(instance));

            Assert.IsFalse(instance.IsSpawned);
        }

        [Test]
        public void RpcThatCannotBeDeliveredIsDroppedSilently()
        {
            (NetworkManager owner, ulong ownerId) = Connect();
            RpcBehaviour onServer = SpawnOwnedBy(ownerId);
            byte[] body = { 1, 0, 0, 0 };

            owner.ClientManager.SendToObject(RpcCodecs.FireId, 99, 1, body);
            owner.ClientManager.SendToObject(RpcCodecs.FireId, onServer.NetworkObject.ObjectId, 7, body);
            owner.ClientManager.SendToObject(RpcCodecs.FireId, onServer.NetworkObject.ObjectId, 0, body);
            RunFrames(3);

            Assert.IsEmpty(onServer.Calls);
            Assert.AreEqual(ConnectionState.Started, owner.ClientManager.State);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void UndecodableRpcIsDroppedAsAReceivedEntry()
        {
            (NetworkManager owner, ulong ownerId) = Connect();
            RpcBehaviour onServer = SpawnOwnedBy(ownerId);
            var dropped = new List<ReceiveDroppedArgs>();
            server.ServerManager.OnReceiveDropped += dropped.Add;

            LogAssert.Expect(LogType.Warning, new Regex("dropped 9 received bytes from peer"));
            owner.ClientManager.SendToObject(RpcCodecs.FireId, onServer.NetworkObject.ObjectId, onServer.BehaviourIndex, new byte[] { 1, 2 });
            Remote(owner, onServer).Fire(3);
            RunFrames(3);

            Assert.AreEqual(1, dropped.Count);
            Assert.AreEqual(9, dropped[0].FrameLength);
            Assert.AreEqual(new[] { $"Fire {ownerId} 3" }, onServer.Calls);
            Assert.AreEqual(ConnectionState.Started, owner.ClientManager.State);
        }

        [Test]
        public void ClientRpcExceptionIsAHandlerException()
        {
            (NetworkManager client, ulong _) = Connect();
            var errors = new List<HandlerExceptionArgs>();
            client.ClientManager.OnHandlerException += errors.Add;
            RpcBehaviour onServer = SpawnOwnedBy(0);

            onServer.Announce(RpcCodecs.ThrowingValue);
            RunFrames(3);

            Assert.AreEqual(1, errors.Count);
            Assert.AreEqual(RpcCodecs.AnnounceId, errors[0].MessageId);
            Assert.AreEqual(ConnectionState.Stopped, client.ClientManager.State);
        }

        [Test]
        public void HostClientSendsAndReceivesRpcsThroughTheSharedInstance()
        {
            var hostNetwork = new GameObject("HostNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(hostNetwork.gameObject);
            NetworkManager host = CreateManager(hostNetwork);
            host.ServerManager.StartConnection(2);
            host.ClientManager.StartConnection("unused.invalid", 2);
            RunFrames(20);
            ulong localPeerId = host.ClientManager.Objects.LocalPeerId;
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            host.ServerManager.Spawn(instance, localPeerId);
            RunFrames(3);
            RpcBehaviour shared = instance.GetComponent<RpcBehaviour>();

            shared.Fire(1);
            shared.Announce(2);
            RunFrames(3);

            Assert.AreSame(instance, host.ClientManager.Spawned[instance.ObjectId]);
            Assert.AreEqual(1, shared.Registrations);
            CollectionAssert.AreEquivalent(new[] { $"Fire {localPeerId} 1", "Announce 2" }, shared.Calls);
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

        private RpcBehaviour SpawnOwnedBy(ulong ownerId)
        {
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            server.ServerManager.Spawn(instance, ownerId);
            RunFrames(3);
            return instance.GetComponent<RpcBehaviour>();
        }

        private static RpcBehaviour Remote(NetworkManager client, RpcBehaviour onServer) =>
            ((NetworkObject)client.ClientManager.Spawned[onServer.NetworkObject.ObjectId]).GetComponent<RpcBehaviour>();

        private NetworkManager CreateManager(NetworkTransport transport)
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("transport").objectReferenceValue = transport;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            FomoxaRegistry registry = TestObjects.Registry(
                new MessageSchema(RpcCodecs.FireId, 0xF011, new ulong[] { 0xF011 }),
                new MessageSchema(RpcCodecs.OpenId, 0xF012, new ulong[] { 0xF012 }),
                new MessageSchema(RpcCodecs.AnnounceId, 0xF013, new ulong[] { 0xF013 }),
                new MessageSchema(RpcCodecs.WhisperId, 0xF014, new ulong[] { 0xF014 }));
            registry.Channels.Set(RpcCodecs.FireId, Channel.ReliableOrdered);
            registry.Channels.Set(RpcCodecs.AnnounceId, Channel.ReliableOrdered);
            manager.Registry = registry;
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
