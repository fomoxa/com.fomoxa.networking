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
    public sealed class DeltaStateTest
    {
        private const uint PrefabId = 0xD1;
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
            prefab.gameObject.AddComponent<WideStateBehaviour>();
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
        public void OneChangedValueTravelsAsADeltaAndRebuildsTheState()
        {
            (NetworkManager client, ulong _) = Connect();
            WideStateBehaviour onServer = Spawn();
            WideStateBehaviour onClient = Remote(client, onServer);

            onServer.State.Values[9] = 0x0102;
            RunFrames(1);
            StateSlot slot = onServer.StateSlot;
            RunFrames(5);

            Assert.IsTrue(slot.HasDelta);
            Assert.AreEqual(new byte[] { 40, 2, 0x02, 0x01 }, slot.Delta.ToArray());
            Assert.Less(4 + slot.Delta.Length, slot.Sent.Length);
            Assert.AreEqual(WideStateBehaviour.Describe(onServer.State), WideStateBehaviour.Describe(onClient.State));
            Assert.AreEqual(new[] { "0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0 -> 0,0,0,0,0,0,0,0,0,258,0,0,0,0,0,0" }, onClient.Changes);
        }

        [Test]
        public void LengthChangeSendsTheFullState()
        {
            (NetworkManager client, ulong _) = Connect();
            WideStateBehaviour onServer = Spawn();
            WideStateBehaviour onClient = Remote(client, onServer);

            onServer.State.Values.Add(5);
            RunFrames(1);
            Assert.IsFalse(onServer.StateSlot.HasDelta);
            RunFrames(5);

            Assert.AreEqual(17, onClient.State.Values.Count);
            Assert.AreEqual(5u, onClient.State.Values[16]);
            Assert.AreEqual(1, onClient.Changes.Count);
        }

        [Test]
        public void DeltaNoSmallerThanTheFullStateSendsTheFullState()
        {
            (NetworkManager client, ulong _) = Connect();
            WideStateBehaviour onServer = Spawn();
            WideStateBehaviour onClient = Remote(client, onServer);

            for (int index = 0; index < 16; index++)
            {
                onServer.State.Values[index] = 0x01010101;
            }

            RunFrames(1);
            StateSlot slot = onServer.StateSlot;
            RunFrames(5);

            Assert.IsTrue(slot.HasDelta);
            Assert.GreaterOrEqual(4 + slot.Delta.Length, slot.Sent.Length);
            Assert.AreEqual(WideStateBehaviour.Describe(onServer.State), WideStateBehaviour.Describe(onClient.State));
            Assert.AreEqual(1, onClient.Changes.Count);
        }

        [Test]
        public void ClientThatLosesItsBaseSkipsDeltasAndIsResyncedWithTheFullState()
        {
            (NetworkManager client, ulong clientId) = Connect();
            var dropped = new List<ReceiveDroppedArgs>();
            client.ClientManager.OnReceiveDropped += dropped.Add;
            WideStateBehaviour onServer = Spawn();
            WideStateBehaviour onClient = Remote(client, onServer);

            LogAssert.Expect(LogType.Warning, new Regex("dropped 9 received bytes"));
            server.ServerManager.SendToObject(clientId, WideStateCodec.WideId, onServer.NetworkObject.ObjectId, onServer.BehaviourIndex, new byte[] { 1, 2 });
            onServer.State.Values[3] = 7;
            RunFrames(1);
            Assert.IsTrue(onServer.StateSlot.HasDelta);
            RunFrames(8);

            Assert.AreEqual(1, dropped.Count);
            Assert.IsFalse(onClient.StateSlot.OutOfSync);
            Assert.AreEqual(WideStateBehaviour.Describe(onServer.State), WideStateBehaviour.Describe(onClient.State));
            Assert.AreEqual(new[] { "0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0 -> 0,0,0,7,0,0,0,0,0,0,0,0,0,0,0,0" }, onClient.Changes);
            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
        }

        [Test]
        public void DeltaThatRunsPastItsBaseIsDroppedAndResynced()
        {
            (NetworkManager client, ulong clientId) = Connect();
            var dropped = new List<ReceiveDroppedArgs>();
            client.ClientManager.OnReceiveDropped += dropped.Add;
            WideStateBehaviour onServer = Spawn();
            WideStateBehaviour onClient = Remote(client, onServer);
            byte[] delta = StateDeltaNetAdapter.Instance.Encode(new StateDelta { Data = new byte[] { 255, 1, 9 } }).ToArray();

            LogAssert.Expect(LogType.Warning, new Regex("dropped 14 received bytes"));
            server.ServerManager.SendToObject(clientId, TestObjects.StateDeltaId, onServer.NetworkObject.ObjectId, onServer.BehaviourIndex, delta);
            RunFrames(8);

            Assert.AreEqual(1, dropped.Count);
            Assert.IsFalse(onClient.StateSlot.OutOfSync);
            Assert.AreEqual(WideStateBehaviour.Describe(onServer.State), WideStateBehaviour.Describe(onClient.State));
            Assert.AreEqual(1, onClient.Changes.Count);
        }

        [Test]
        public void ResyncForAnObjectOrBehaviourWithoutStateIsIgnored()
        {
            (NetworkManager client, ulong _) = Connect();
            WideStateBehaviour onServer = Spawn();

            client.ClientManager.SendToObject(TestObjects.StateResyncId, 99, 0, ReadOnlySpan<byte>.Empty);
            client.ClientManager.SendToObject(TestObjects.StateResyncId, onServer.NetworkObject.ObjectId, 0, ReadOnlySpan<byte>.Empty);
            client.ClientManager.SendToObject(TestObjects.StateResyncId, onServer.NetworkObject.ObjectId, 7, ReadOnlySpan<byte>.Empty);
            RunFrames(5);

            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
            Assert.IsEmpty(Remote(client, onServer).Changes);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LatePeerStartsFromTheLastSentStateAndFollowsLaterDeltas()
        {
            (NetworkManager early, ulong _) = Connect();
            WideStateBehaviour onServer = Spawn();
            onServer.State.Values[1] = 11;
            RunFrames(3);

            (NetworkManager late, ulong _) = Connect();
            onServer.State.Values[2] = 22;
            RunFrames(5);

            Assert.AreEqual("0,11,0,0,0,0,0,0,0,0,0,0,0,0,0,0", Remote(late, onServer).ValuesAtStartClient);
            Assert.AreEqual(WideStateBehaviour.Describe(onServer.State), WideStateBehaviour.Describe(Remote(late, onServer).State));
            Assert.AreEqual(WideStateBehaviour.Describe(onServer.State), WideStateBehaviour.Describe(Remote(early, onServer).State));
            Assert.AreEqual(2, Remote(early, onServer).Changes.Count);
            Assert.AreEqual(1, Remote(late, onServer).Changes.Count);
        }

        [Test]
        public void HostClientRebuildsThePreviousStateFromDeltas()
        {
            var hostNetwork = new GameObject("HostNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(hostNetwork.gameObject);
            NetworkManager host = CreateManager(hostNetwork);
            host.ServerManager.StartConnection(2);
            host.ClientManager.StartConnection("unused.invalid", 2);
            RunFrames(20);
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            WideStateBehaviour shared = instance.GetComponent<WideStateBehaviour>();
            host.ServerManager.Spawn(instance);
            RunFrames(3);

            shared.State.Values[0] = 1;
            RunFrames(3);
            shared.State.Values[1] = 2;
            RunFrames(3);

            Assert.AreEqual(
                new[]
                {
                    "0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0 -> 1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0",
                    "1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0 -> 1,2,0,0,0,0,0,0,0,0,0,0,0,0,0,0",
                },
                shared.Changes);
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

        private WideStateBehaviour Spawn()
        {
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            server.ServerManager.Spawn(instance);
            RunFrames(3);
            return instance.GetComponent<WideStateBehaviour>();
        }

        private static WideStateBehaviour Remote(NetworkManager client, WideStateBehaviour onServer) =>
            ((NetworkObject)client.ClientManager.Spawned[onServer.NetworkObject.ObjectId]).GetComponent<WideStateBehaviour>();

        private NetworkManager CreateManager(NetworkTransport transport)
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("transport").objectReferenceValue = transport;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            FomoxaRegistry registry = TestObjects.Registry(new MessageSchema(WideStateCodec.WideId, 0xF034, new ulong[] { 0xF034 }));
            registry.Channels.Set(WideStateCodec.WideId, Channel.ReliableOrdered);
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
