using System.Collections.Generic;
using System;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking;
using Fomoxa.Networking.Objects;
using Fomoxa.Unity.Tests.Support;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Fomoxa.Unity.Tests
{
    public sealed class StateTest
    {
        private const uint PrefabId = 0xC1;
        private const uint InvalidPrefabId = 0xC2;
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
            prefab.gameObject.AddComponent<StateBehaviour>();
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
        public void InitialStateIsInPlaceBeforeOnStartClientAndIsNotReportedAsAChange()
        {
            (NetworkManager client, ulong _) = Connect();

            StateBehaviour onServer = Spawn(7);
            StateBehaviour onClient = Remote(client, onServer);

            Assert.AreEqual(1, onClient.BehaviourIndex);
            Assert.AreEqual(7u, onClient.ValueAtStartClient);
            Assert.AreEqual(7u, onClient.State.Value);
            Assert.IsEmpty(onClient.Changes);
        }

        [Test]
        public void ChangeIsSentOnceAndReportsThePreviousState()
        {
            (NetworkManager client, ulong _) = Connect();
            var dropped = new List<ReceiveDroppedArgs>();
            client.ClientManager.OnReceiveDropped += dropped.Add;
            StateBehaviour onServer = Spawn(7);
            StateBehaviour onClient = Remote(client, onServer);

            onServer.State.Value = 8;
            RunFrames(5);
            onServer.State.Spare = 1;
            onServer.State.Spare = 0;
            RunFrames(5);

            Assert.AreEqual(8u, onClient.State.Value);
            Assert.AreEqual(new[] { "7->8" }, onClient.Changes);
            Assert.IsEmpty(onServer.Changes);
            Assert.IsEmpty(dropped);
        }

        [Test]
        public void LatePeerGetsTheCurrentStateInItsSpawn()
        {
            (NetworkManager early, ulong _) = Connect();
            StateBehaviour onServer = Spawn(7);
            onServer.State.Value = 9;
            RunFrames(5);

            (NetworkManager late, ulong _) = Connect();
            RunFrames(5);

            Assert.AreEqual(new[] { "7->9" }, Remote(early, onServer).Changes);
            Assert.AreEqual(9u, Remote(late, onServer).ValueAtStartClient);
            Assert.IsEmpty(Remote(late, onServer).Changes);
        }

        [Test]
        public void ChangeOnTheClientIsOverwrittenByTheNextStateFromTheServer()
        {
            (NetworkManager client, ulong _) = Connect();
            StateBehaviour onServer = Spawn(7);
            StateBehaviour onClient = Remote(client, onServer);

            onClient.State.Value = 100;
            onServer.State.Value = 8;
            RunFrames(5);

            Assert.AreEqual(8u, onClient.State.Value);
            Assert.AreEqual(new[] { "7->8" }, onClient.Changes);
        }

        [Test]
        public void UndecodableStateIsDroppedAndKeepsTheCurrentState()
        {
            (NetworkManager client, ulong clientId) = Connect();
            var dropped = new List<ReceiveDroppedArgs>();
            client.ClientManager.OnReceiveDropped += dropped.Add;
            StateBehaviour onServer = Spawn(7);
            StateBehaviour onClient = Remote(client, onServer);

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("dropped 10 received bytes"));
            server.ServerManager.SendToObject(clientId, StateCodecs.CounterId, onServer.NetworkObject.ObjectId, onServer.BehaviourIndex, new byte[] { 1, 2, 3 });
            onServer.State.Value = 8;
            RunFrames(5);

            Assert.AreEqual(1, dropped.Count);
            Assert.AreEqual(new[] { "7->8" }, onClient.Changes);
            Assert.AreEqual(8u, onClient.State.Value);
            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
        }

        [Test]
        public void HostClientIsToldOfChangesWithoutDecodingIntoTheSharedInstance()
        {
            var hostNetwork = new GameObject("HostNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(hostNetwork.gameObject);
            NetworkManager host = CreateManager(hostNetwork);
            host.ServerManager.StartConnection(2);
            host.ClientManager.StartConnection("unused.invalid", 2);
            RunFrames(20);
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            StateBehaviour shared = instance.GetComponent<StateBehaviour>();
            shared.State.Value = 1;
            host.ServerManager.Spawn(instance);
            RunFrames(3);

            shared.State.Value = 2;
            RunFrames(1);
            shared.State.Value = 3;
            RunFrames(5);

            Assert.AreEqual(1u, shared.ValueAtStartClient);
            Assert.AreEqual(3u, shared.State.Value);
            Assert.AreEqual(new[] { "1->3", "2->3" }, shared.Changes);
        }

        [TestCase(InvalidStateUse.Unreliable, "is not on the reliable-ordered channel")]
        [TestCase(InvalidStateUse.Empty, "encodes to no bytes")]
        [TestCase(InvalidStateUse.Twice, "uses more than one state model")]
        [TestCase(InvalidStateUse.AlsoAnRpc, "is already a server RPC")]
        public void InvalidStateDeclarationThrowsAndTheObjectIsNotSpawned(InvalidStateUse mode, string message)
        {
            NetworkObject invalid = TestPrefabs.Create("Invalid", InvalidPrefabId);
            invalid.gameObject.AddComponent<InvalidStateBehaviour>().Mode = mode;
            server.Prefabs.Register(invalid);
            NetworkObject instance = UnityEngine.Object.Instantiate(invalid);

            HandlerRegistrationException error = Assert.Throws<HandlerRegistrationException>(() => server.ServerManager.Spawn(instance));

            StringAssert.Contains(message, error.Message);
            Assert.IsFalse(instance.IsSpawned);
        }

        [Test]
        public void SpawnStatesMustMatchTheStatesOfTheBehaviours()
        {
            byte[] valid = StateCodecs.Counter.Encode(new CounterState { Value = 5 }).ToArray();
            ReadOnlyMemory<byte> none = ReadOnlyMemory<byte>.Empty;

            Assert.IsFalse(ClientEntities.TryApplyStates(Behaviours(Registered()), new ReadOnlyMemory<byte>[] { none }, false));
            Assert.IsFalse(ClientEntities.TryApplyStates(Behaviours(Registered()), new ReadOnlyMemory<byte>[] { none, none }, false));
            Assert.IsFalse(ClientEntities.TryApplyStates(Behaviours(Registered()), new ReadOnlyMemory<byte>[] { valid, valid }, false));
            Assert.IsFalse(ClientEntities.TryApplyStates(Behaviours(Registered()), new ReadOnlyMemory<byte>[] { none, new byte[] { 1, 2 } }, false));
            NetworkObject accepted = Registered();
            Assert.IsTrue(ClientEntities.TryApplyStates(Behaviours(accepted), new ReadOnlyMemory<byte>[] { none, valid }, false));
            Assert.AreEqual(5u, accepted.GetComponent<StateBehaviour>().State.Value);
            NetworkObject shared = Registered();
            Assert.IsTrue(ClientEntities.TryApplyStates(Behaviours(shared), new ReadOnlyMemory<byte>[] { none, valid }, true));
            Assert.AreEqual(0u, shared.GetComponent<StateBehaviour>().State.Value);
        }

        private NetworkObject Registered()
        {
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            instance.CollectBehaviours();
            foreach (EntityBehaviour behaviour in Behaviours(instance))
            {
                behaviour.Register(new RpcMessageIds(), server.Registry.Channels, TestObjects.StateProtocol(server.Registry.Channels), new InputRules());
            }

            return instance;
        }

        private static IReadOnlyList<EntityBehaviour> Behaviours(NetworkObject instance) => ((INetworkEntity)instance).EntityBehaviours;

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

        private StateBehaviour Spawn(uint value)
        {
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            StateBehaviour onServer = instance.GetComponent<StateBehaviour>();
            onServer.State.Value = value;
            server.ServerManager.Spawn(instance);
            RunFrames(3);
            return onServer;
        }

        private static StateBehaviour Remote(NetworkManager client, StateBehaviour onServer) =>
            ((NetworkObject)client.ClientManager.Spawned[onServer.NetworkObject.ObjectId]).GetComponent<StateBehaviour>();

        private NetworkManager CreateManager(NetworkTransport transport)
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("transport").objectReferenceValue = transport;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            FomoxaRegistry registry = TestObjects.Registry(
                new MessageSchema(StateCodecs.CounterId, 0xF031, new ulong[] { 0xF031 }),
                new MessageSchema(StateCodecs.UnreliableId, 0xF032, new ulong[] { 0xF032 }),
                new MessageSchema(StateCodecs.EmptyId, 0xF033, new ulong[] { 0xF033 }));
            registry.Channels.Set(StateCodecs.CounterId, Channel.ReliableOrdered);
            registry.Channels.Set(StateCodecs.EmptyId, Channel.ReliableOrdered);
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
