using System.Collections.Generic;
using System.Linq;
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
    public sealed class ObserversTest
    {
        private const uint PrefabId = 0xB1;
        private const ulong SceneObjectId = 0xABCD_0001_0000_00B1;
        private const double FrameSeconds = 1.0 / 30;

        private readonly List<GameObject> created = new List<GameObject>();
        private readonly List<NetworkManager> managers = new List<NetworkManager>();
        private InMemoryNetworkTransport network;
        private NetworkManager server;
        private NetworkObject prefab;
        private TestObserverRule rule;
        private TimeSpan now;

        [SetUp]
        public void CreateServer()
        {
            RecordingBehaviour.Clear();
            now = TimeSpan.Zero;
            network = new GameObject("InMemoryNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(network.gameObject);
            rule = ScriptableObject.CreateInstance<TestObserverRule>();
            prefab = CreatePrefab(TestPrefabs.Create("Prefab", PrefabId));
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
        public void ThePeerTheRuleHidesGetsNothingUntilARebuildShowsTheObject()
        {
            (NetworkManager client, ulong peerId) = Connect();
            rule.Hidden.Add(peerId);
            NetworkObject instance = Spawn();
            RpcBehaviour rpc = instance.GetComponent<RpcBehaviour>();

            Assert.AreEqual(0, rpc.Announce(1));
            Assert.AreEqual(SendResult.NotObserver, rpc.Whisper(peerId, 2));
            Assert.IsFalse(server.ServerManager.IsObserver(instance, peerId));
            RunFrames(3);
            Assert.AreEqual(0, client.ClientManager.Spawned.Count);

            rule.Hidden.Clear();
            Assert.IsTrue(server.ServerManager.RebuildObservers(instance));
            Assert.AreEqual(SendResult.Queued, rpc.Whisper(peerId, 3));
            RunFrames(3);

            NetworkObject remote = (NetworkObject)client.ClientManager.Spawned[instance.ObjectId];
            CollectionAssert.AreEqual(new[] { "Whisper 3" }, remote.GetComponent<RpcBehaviour>().Calls);
            CollectionAssert.Contains(RecordingBehaviour.Log, $"{instance.ObjectId} StartClient");
        }

        [Test]
        public void TheRuleAssetIsTheServerRuleAndADestroyedOrMissingAssetActsAsNoRule()
        {
            (NetworkManager _, ulong peerId) = Connect();
            rule.HideAll = true;

            Assert.AreSame(rule, server.ServerManager.ObserverRule);
            Assert.IsFalse(server.ServerManager.IsObserver(Spawn(), peerId));

            TestObserverRule destroyed = rule;
            UnityEngine.Object.DestroyImmediate(destroyed);
            rule = ScriptableObject.CreateInstance<TestObserverRule>();

            Assert.IsTrue(server.ServerManager.IsObserver(Spawn(), peerId));

            server.ServerManager.ObserverRule = null;

            Assert.IsNull(server.ServerManager.ObserverRule);
            Assert.IsTrue(server.ServerManager.IsObserver(Spawn(), peerId));
        }

        [Test]
        public void VisibilityEveryoneIgnoresTheRuleAndOwnerOnlyShowsOnlyTheOwner()
        {
            (NetworkManager owner, ulong ownerId) = Connect();
            (NetworkManager other, ulong otherId) = Connect();
            rule.HideAll = true;

            NetworkObject everyone = UnityEngine.Object.Instantiate(prefab);
            everyone.Visibility = NetworkVisibility.Everyone;
            server.ServerManager.Spawn(everyone);
            NetworkObject ownerOnly = UnityEngine.Object.Instantiate(prefab);
            ownerOnly.Visibility = NetworkVisibility.OwnerOnly;
            rule.HideAll = false;
            server.ServerManager.Spawn(ownerOnly, ownerId);
            RunFrames(3);

            Assert.IsTrue(owner.ClientManager.Spawned.ContainsKey(everyone.ObjectId));
            Assert.IsTrue(other.ClientManager.Spawned.ContainsKey(everyone.ObjectId));
            Assert.IsTrue(owner.ClientManager.Spawned.ContainsKey(ownerOnly.ObjectId));
            Assert.IsFalse(other.ClientManager.Spawned.ContainsKey(ownerOnly.ObjectId));
            Assert.IsFalse(server.ServerManager.IsObserver(ownerOnly, otherId));
        }

        [Test]
        public void ARuleThatThrowsIsLoggedAndHidesTheObject()
        {
            (NetworkManager client, ulong _) = Connect();
            rule.Throws = true;
            LogAssert.Expect(LogType.Exception, new Regex("rule failed"));

            server.ServerManager.Spawn(UnityEngine.Object.Instantiate(prefab));
            rule.Throws = false;
            rule.HideAll = true;
            RunFrames(3);

            Assert.AreEqual(0, client.ClientManager.Spawned.Count);
        }

        [Test]
        public void TheTickRoundAppliesARuleChangeWithinTheInterval()
        {
            (NetworkManager client, ulong peerId) = Connect();
            server.ServerManager.ObserverInterval = 2;
            NetworkObject instance = Spawn();
            Assert.IsTrue(client.ClientManager.Spawned.ContainsKey(instance.ObjectId));

            rule.Hidden.Add(peerId);
            RunFrames(5);

            Assert.IsFalse(client.ClientManager.Spawned.ContainsKey(instance.ObjectId));
            CollectionAssert.Contains(RecordingBehaviour.Log, $"{instance.ObjectId} StopClient");
            Assert.IsFalse(RecordingBehaviour.Log.Contains($"{instance.ObjectId} StopServer"));
        }

        [Test]
        public void APeerThatEntersGetsTheCurrentStateAndLaterDeltasApply()
        {
            (NetworkManager client, ulong peerId) = Connect();
            rule.Hidden.Add(peerId);
            NetworkObject instance = Spawn();
            WideStateBehaviour onServer = instance.GetComponent<WideStateBehaviour>();
            onServer.State.Values[3] = 7;
            RunFrames(3);

            rule.Hidden.Clear();
            server.ServerManager.RebuildObservers(instance);
            RunFrames(3);
            WideStateBehaviour onClient = ((NetworkObject)client.ClientManager.Spawned[instance.ObjectId]).GetComponent<WideStateBehaviour>();
            Assert.AreEqual(WideStateBehaviour.Describe(onServer.State), onClient.ValuesAtStartClient);

            onServer.State.Values[5] = 9;
            RunFrames(3);

            Assert.IsTrue(onServer.StateSlot.HasDelta);
            Assert.AreEqual(WideStateBehaviour.Describe(onServer.State), WideStateBehaviour.Describe(onClient.State));
        }

        [Test]
        public void AChildTransformThatMovedWhileHiddenArrivesWithTheEntry()
        {
            (NetworkManager client, ulong peerId) = Connect();
            rule.Hidden.Add(peerId);
            NetworkObject instance = Spawn();
            Transform child = instance.GetComponentInChildren<NetworkTransform>().transform;
            child.localPosition = new Vector3(4, 0, 0);
            RunFrames(5);

            rule.Hidden.Clear();
            server.ServerManager.RebuildObservers(instance);
            RunFrames(3);

            NetworkTransform onClient = ((NetworkObject)client.ClientManager.Spawned[instance.ObjectId]).GetComponentInChildren<NetworkTransform>();
            Assert.AreEqual(new Vector3(4, 0, 0), onClient.transform.localPosition);
        }

        [Test]
        public void ASceneObjectThatEntersAgainStartsWithACleanReceiveState()
        {
            NetworkObject onServer = CreatePrefab(TestPrefabs.CreateSceneObject("ServerScene", SceneObjectId));
            NetworkObject onClient = CreatePrefab(TestPrefabs.CreateSceneObject("ClientScene", SceneObjectId));
            server.ServerManager.StopConnection();
            server.FindServerSceneObjects = () => new List<NetworkObject> { onServer };
            server.ServerManager.StartConnection(1);
            (NetworkManager client, ulong peerId) = Connect(manager => manager.ClientManager.FindSceneObjects = () => new List<NetworkObject> { onClient });
            Assert.AreSame(onClient, client.ClientManager.Spawned[onServer.ObjectId]);
            NetworkTransform transform = onClient.GetComponentInChildren<NetworkTransform>();
            transform.Receive(1_000_000, 1, Vector3.zero, Quaternion.identity, Vector3.one, true, 0, 30);
            onClient.GetComponent<WideStateBehaviour>().StateSlot.MarkOutOfSync();

            rule.Hidden.Add(peerId);
            server.ServerManager.RebuildObservers(onServer);
            RunFrames(3);
            Assert.IsFalse(onClient.gameObject.activeSelf);

            rule.Hidden.Clear();
            onServer.GetComponentInChildren<NetworkTransform>().transform.localPosition = new Vector3(2, 0, 0);
            server.ServerManager.RebuildObservers(onServer);
            RunFrames(3);
            onServer.GetComponent<WideStateBehaviour>().State.Values[1] = 5;
            RunFrames(3);

            Assert.IsTrue(onClient.gameObject.activeSelf);
            Assert.IsFalse(onClient.GetComponent<WideStateBehaviour>().StateSlot.OutOfSync);
            Assert.AreEqual(5u, onClient.GetComponent<WideStateBehaviour>().State.Values[1]);
            Assert.Less(transform.LastTick, 1_000_000u);
            Assert.AreEqual(new Vector3(2, 0, 0), transform.transform.localPosition);
        }

        [Test]
        public void TheHostHidesWhatItsClientDoesNotObserveWithoutRestartingCallbacks()
        {
            (NetworkManager host, ulong localPeerId) = StartHost();
            rule.Hidden.Add(localPeerId);
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            Renderer renderer = instance.GetComponentInChildren<Renderer>();
            host.ServerManager.Spawn(instance);
            RunFrames(3);
            uint id = instance.ObjectId;

            Assert.IsTrue(instance.HiddenOnHost);
            Assert.IsFalse(renderer.enabled);
            CollectionAssert.AreEqual(new[] { $"{id} StartServer", $"{id} HostVisibility False" }, RecordingBehaviour.Log);

            rule.Hidden.Clear();
            host.ServerManager.RebuildObservers(instance);
            RunFrames(3);
            rule.Hidden.Add(localPeerId);
            host.ServerManager.RebuildObservers(instance);
            RunFrames(3);
            rule.Hidden.Clear();
            host.ServerManager.RebuildObservers(instance);
            RunFrames(3);

            Assert.IsTrue(renderer.enabled);
            Assert.AreSame(instance, host.ClientManager.Spawned[id]);
            CollectionAssert.AreEqual(
                new[]
                {
                    $"{id} StartServer", $"{id} HostVisibility False",
                    $"{id} StartClient", $"{id} HostVisibility True",
                    $"{id} HostVisibility False",
                    $"{id} HostVisibility True",
                },
                RecordingBehaviour.Log);
        }

        [Test]
        public void AnExceptionFromOnHostVisibilityIsLoggedAndTheHostClientKeepsRunning()
        {
            (NetworkManager host, ulong localPeerId) = StartHost();
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            host.ServerManager.Spawn(instance);
            RunFrames(3);
            RecordingBehaviour.ThrowOn = "HostVisibility";
            LogAssert.Expect(LogType.Exception, new Regex("HostVisibility failed"));

            rule.Hidden.Add(localPeerId);
            host.ServerManager.RebuildObservers(instance);
            RunFrames(3);

            Assert.AreEqual(ConnectionState.Started, host.ClientManager.State);
            Assert.IsTrue(instance.HiddenOnHost);
            Assert.IsFalse(instance.GetComponentInChildren<Renderer>().enabled);
            Assert.AreSame(instance, host.ClientManager.Spawned[instance.ObjectId]);
            RecordingBehaviour.ThrowOn = null;
        }

        [Test]
        public void AResyncFromAPeerOutsideTheSetGetsNoState()
        {
            (NetworkManager client, ulong peerId) = Connect();
            rule.Hidden.Add(peerId);
            NetworkObject instance = Spawn();
            WideStateBehaviour onServer = instance.GetComponent<WideStateBehaviour>();
            var received = new List<byte[]>();
            client.ClientManager.Dispatcher.RegisterObject(WideStateCodec.WideId, (sender, objectId, behaviourIndex, body) => received.Add(body.ToArray()));

            client.ClientManager.SendToObject(TestObjects.StateResyncId, instance.ObjectId, onServer.BehaviourIndex, ReadOnlySpan<byte>.Empty);
            RunFrames(5);

            Assert.AreEqual(0, received.Count);
        }

        [Test]
        public void TheHostRestoresOnlyTheRenderersItTurnedOff()
        {
            (NetworkManager host, ulong localPeerId) = StartHost();
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            var extra = new GameObject("Extra");
            extra.transform.SetParent(instance.transform, false);
            Renderer turnedOff = extra.AddComponent<MeshRenderer>();
            turnedOff.enabled = false;
            host.ServerManager.Spawn(instance);
            RunFrames(3);

            rule.Hidden.Add(localPeerId);
            host.ServerManager.RebuildObservers(instance);
            RunFrames(3);
            rule.Hidden.Clear();
            host.ServerManager.RebuildObservers(instance);
            RunFrames(3);

            Assert.IsFalse(instance.HiddenOnHost);
            Assert.IsTrue(instance.GetComponentInChildren<NetworkTransform>().GetComponent<Renderer>().enabled);
            Assert.IsFalse(turnedOff.enabled);
        }

        [Test]
        public void TheHostShowsEverythingWhenItsClientStops()
        {
            (NetworkManager host, ulong localPeerId) = StartHost();
            NetworkObject started = UnityEngine.Object.Instantiate(prefab);
            host.ServerManager.Spawn(started);
            RunFrames(3);
            rule.Hidden.Add(localPeerId);
            host.ServerManager.RebuildObservers(started);
            NetworkObject neverSeen = UnityEngine.Object.Instantiate(prefab);
            host.ServerManager.Spawn(neverSeen);
            RunFrames(3);
            RecordingBehaviour.Log.Clear();

            host.ClientManager.StopConnection();
            RunFrames(2);

            Assert.IsFalse(started.HiddenOnHost);
            Assert.IsFalse(neverSeen.HiddenOnHost);
            Assert.IsTrue(neverSeen.GetComponentInChildren<Renderer>().enabled);
            Assert.AreEqual(0, host.ClientManager.Spawned.Count);
            CollectionAssert.AreEquivalent(
                new[] { $"{started.ObjectId} StopClient", $"{started.ObjectId} HostVisibility True", $"{neverSeen.ObjectId} HostVisibility True" },
                RecordingBehaviour.Log);
        }

        [Test]
        public void TheHostClientHidesObjectsSpawnedBeforeItStarted()
        {
            var hostNetwork = new GameObject("HostNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(hostNetwork.gameObject);
            NetworkManager host = CreateManager(hostNetwork);
            host.ServerManager.StartConnection(2);
            rule.HideAll = true;
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            host.ServerManager.Spawn(instance);

            host.ClientManager.StartConnection("unused.invalid", 2);
            RunFrames(20);

            Assert.IsTrue(instance.HiddenOnHost);
            Assert.IsFalse(host.ClientManager.Spawned.ContainsKey(instance.ObjectId));
            Assert.IsFalse(RecordingBehaviour.Log.Contains($"{instance.ObjectId} StartClient"));
        }

        private NetworkObject CreatePrefab(NetworkObject networkObject)
        {
            networkObject.gameObject.AddComponent<WideStateBehaviour>();
            networkObject.gameObject.AddComponent<RpcBehaviour>();
            var child = new GameObject("Child");
            child.transform.SetParent(networkObject.transform, false);
            child.AddComponent<NetworkTransform>();
            child.AddComponent<MeshRenderer>();
            return networkObject;
        }

        private (NetworkManager Manager, ulong LocalPeerId) StartHost()
        {
            var hostNetwork = new GameObject("HostNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(hostNetwork.gameObject);
            NetworkManager host = CreateManager(hostNetwork);
            host.ServerManager.StartConnection(2);
            host.ClientManager.StartConnection("unused.invalid", 2);
            RunFrames(20);
            Assert.AreEqual(ConnectionState.Started, host.ClientManager.State);
            return (host, host.ClientManager.Objects.LocalPeerId);
        }

        private (NetworkManager Manager, ulong PeerId) Connect(Action<NetworkManager> configure = null)
        {
            ulong peerId = 0;
            Action<ConnectionStateArgs> record = args => peerId = args.PeerId;
            server.ServerManager.OnRemoteConnectionState += record;
            NetworkManager client = CreateManager(network);
            configure?.Invoke(client);
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(20);
            server.ServerManager.OnRemoteConnectionState -= record;
            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
            return (client, peerId);
        }

        private NetworkObject Spawn()
        {
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            server.ServerManager.Spawn(instance);
            RunFrames(3);
            return instance;
        }

        private NetworkManager CreateManager(NetworkTransport transport)
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("transport").objectReferenceValue = transport;
            serialized.FindProperty("observerRule").objectReferenceValue = rule;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            FomoxaRegistry registry = TestObjects.Registry(
                new MessageSchema(WideStateCodec.WideId, 0xF034, new ulong[] { 0xF034 }),
                new MessageSchema(RpcCodecs.FireId, 0xF011, new ulong[] { 0xF011 }),
                new MessageSchema(RpcCodecs.OpenId, 0xF012, new ulong[] { 0xF012 }),
                new MessageSchema(RpcCodecs.AnnounceId, 0xF013, new ulong[] { 0xF013 }),
                new MessageSchema(RpcCodecs.WhisperId, 0xF014, new ulong[] { 0xF014 }));
            registry.Channels.Set(WideStateCodec.WideId, Channel.ReliableOrdered);
            registry.Channels.Set(RpcCodecs.FireId, Channel.ReliableOrdered);
            registry.Channels.Set(RpcCodecs.AnnounceId, Channel.ReliableOrdered);
            registry.Channels.Set(RpcCodecs.WhisperId, Channel.ReliableOrdered);
            manager.Registry = registry;
            manager.Initialize();
            manager.FindServerSceneObjects = () => new List<NetworkObject>();
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
