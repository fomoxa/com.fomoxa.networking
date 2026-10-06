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
    public sealed class ParameterlessRpcTest
    {
        private const uint PrefabId = 0xB1;
        private const uint DerivedPrefabId = 0xB2;
        private const uint UnregisteredPrefabId = 0xB3;
        private const double FrameSeconds = 1.0 / 30;

        private readonly List<GameObject> created = new List<GameObject>();
        private readonly List<NetworkManager> managers = new List<NetworkManager>();
        private InMemoryNetworkTransport network;
        private NetworkManager server;
        private NetworkObject prefab;
        private NetworkObject derivedPrefab;
        private TimeSpan now;

        [SetUp]
        public void CreateServer()
        {
            now = TimeSpan.Zero;
            network = new GameObject("InMemoryNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(network.gameObject);
            prefab = TestPrefabs.Create("Prefab", PrefabId);
            prefab.gameObject.AddComponent<EmptyRpcBehaviour>();
            derivedPrefab = TestPrefabs.Create("Derived", DerivedPrefabId);
            derivedPrefab.gameObject.AddComponent<DerivedEmptyRpcBehaviour>();
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
        }

        [Test]
        public void ServerRpcWithoutAModelRunsForTheOwnerAndForAnyPeerWhenOwnershipIsOff()
        {
            (NetworkManager owner, ulong ownerId) = Connect();
            (NetworkManager other, ulong otherId) = Connect();
            EmptyRpcBehaviour onServer = SpawnOwnedBy(prefab, ownerId);

            LogAssert.Expect(LogType.Warning, new Regex($"dropped server RPC 0x20000021 to object {onServer.NetworkObject.ObjectId} from peer {otherId}, which does not own"));
            Assert.AreEqual(SendResult.Queued, Remote(owner, onServer).RequestJump());
            Remote(other, onServer).RequestJump();
            Remote(other, onServer).RequestWave();
            RunFrames(3);

            Assert.AreEqual(new[] { $"Jump {ownerId}", $"Wave {otherId}" }, onServer.Calls);
        }

        [Test]
        public void ObserversRpcWithoutAModelReachesEveryClientAndTargetRpcOnlyOne()
        {
            (NetworkManager first, ulong firstId) = Connect();
            (NetworkManager second, ulong _) = Connect();
            EmptyRpcBehaviour onServer = SpawnOwnedBy(prefab, 0);

            Assert.AreEqual(2, onServer.PingObservers());
            Assert.AreEqual(SendResult.Queued, onServer.PingTarget(firstId));
            RunFrames(3);

            Assert.AreEqual(new[] { "Ping", "Ping" }, Remote(first, onServer).Calls);
            Assert.AreEqual(new[] { "Ping" }, Remote(second, onServer).Calls);
            Assert.IsEmpty(onServer.Calls);
        }

        [Test]
        public void BytesAfterTheEmptyModelAreIgnored()
        {
            (NetworkManager owner, ulong ownerId) = Connect();
            EmptyRpcBehaviour onServer = SpawnOwnedBy(prefab, ownerId);

            owner.ClientManager.SendToObject(EmptyRpcBehaviour.JumpId, onServer.NetworkObject.ObjectId, onServer.BehaviourIndex, new byte[] { 1, 2, 3 });
            RunFrames(3);

            Assert.AreEqual(new[] { $"Jump {ownerId}" }, onServer.Calls);
        }

        [Test]
        public void SubclassUsesTheRpcsOfItsBaseClass()
        {
            (NetworkManager owner, ulong ownerId) = Connect();
            EmptyRpcBehaviour onServer = SpawnOwnedBy(derivedPrefab, ownerId);

            Remote(owner, onServer).RequestJump();
            onServer.PingObservers();
            RunFrames(3);

            Assert.IsInstanceOf<DerivedEmptyRpcBehaviour>(onServer);
            Assert.AreEqual(new[] { $"Jump {ownerId}" }, onServer.Calls);
            Assert.AreEqual(new[] { "Ping" }, Remote(owner, onServer).Calls);
        }

        [Test]
        public void RegisteringAnRpcWithoutAGeneratedModelThrowsAndTheObjectIsNotSpawned()
        {
            NetworkObject unregistered = TestPrefabs.Create("Unregistered", UnregisteredPrefabId);
            unregistered.gameObject.AddComponent<UnregisteredRpcBehaviour>();
            server.Prefabs.Register(unregistered);
            NetworkObject instance = UnityEngine.Object.Instantiate(unregistered);

            HandlerRegistrationException error = Assert.Throws<HandlerRegistrationException>(() => server.ServerManager.Spawn(instance));

            StringAssert.Contains($"{typeof(UnregisteredRpcBehaviour).FullName}.Hop has no generated parameterless RPC model", error.Message);
            Assert.IsFalse(instance.IsSpawned);
        }

        [Test]
        public void SendingANameWithoutAGeneratedModelThrows()
        {
            (NetworkManager owner, ulong ownerId) = Connect();
            EmptyRpcBehaviour onServer = SpawnOwnedBy(prefab, ownerId);
            EmptyRpcBehaviour onOwner = Remote(owner, onServer);

            Assert.Throws<InvalidOperationException>(() => onOwner.SendServerRpcNamed("Missing"));
            Assert.Throws<InvalidOperationException>(() => onServer.SendObserversRpcNamed("Missing"));
            Assert.Throws<InvalidOperationException>(() => onServer.SendTargetRpcNamed(ownerId, "Missing"));
            Assert.Throws<ArgumentNullException>(() => onOwner.SendServerRpcNamed(null));
            Assert.Throws<ArgumentNullException>(() => onServer.SendObserversRpcNamed(null));
            Assert.Throws<ArgumentNullException>(() => onServer.SendTargetRpcNamed(ownerId, null));
            Assert.Throws<InvalidOperationException>(() => onServer.RequestJump());
            Assert.Throws<InvalidOperationException>(() => onOwner.PingObservers());
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

        private EmptyRpcBehaviour SpawnOwnedBy(NetworkObject source, ulong ownerId)
        {
            NetworkObject instance = UnityEngine.Object.Instantiate(source);
            server.ServerManager.Spawn(instance, ownerId);
            RunFrames(3);
            return instance.GetComponent<EmptyRpcBehaviour>();
        }

        private static EmptyRpcBehaviour Remote(NetworkManager client, EmptyRpcBehaviour onServer) =>
            ((NetworkObject)client.ClientManager.Spawned[onServer.NetworkObject.ObjectId]).GetComponent<EmptyRpcBehaviour>();

        private NetworkManager CreateManager(NetworkTransport transport)
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("transport").objectReferenceValue = transport;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            FomoxaRegistry registry = TestObjects.Registry(
                new MessageSchema(EmptyRpcBehaviour.JumpId, 0xF021, new ulong[] { 0xF021 }),
                new MessageSchema(EmptyRpcBehaviour.WaveId, 0xF022, new ulong[] { 0xF022 }),
                new MessageSchema(EmptyRpcBehaviour.PingId, 0xF023, new ulong[] { 0xF023 }));
            registry.Channels.Set(EmptyRpcBehaviour.JumpId, Channel.ReliableOrdered);
            registry.Channels.Set(EmptyRpcBehaviour.PingId, Channel.ReliableOrdered);
            EmptyRpcBehaviour.Declare(registry.Rpcs);
            manager.Registry = registry;
            manager.Initialize();
            manager.FindServerSceneObjects = () => new List<NetworkObject>();
            manager.FindClientSceneObjects = () => new List<NetworkObject>();
            manager.Prefabs.Register(prefab);
            manager.Prefabs.Register(derivedPrefab);
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
