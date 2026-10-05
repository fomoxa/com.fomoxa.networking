using System.Collections.Generic;
using System.Text.RegularExpressions;
using System;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking;
using Fomoxa.Unity.Tests.Support;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;
using UnityEngine;

namespace Fomoxa.Unity.Tests
{
    public sealed class GeneratedRpcTest
    {
        private const uint PrefabId = 0xB4;
        private const uint DerivedPrefabId = 0xB5;
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
            prefab = TestPrefabs.Create("Generated", PrefabId);
            prefab.gameObject.AddComponent<GeneratedRpcBehaviour>();
            derivedPrefab = TestPrefabs.Create("DerivedGenerated", DerivedPrefabId);
            derivedPrefab.gameObject.AddComponent<DerivedGeneratedRpcBehaviour>();
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
        public void ServerRpcsFromAttributesReceivePeerIdAndModelAndFollowRequireOwnership()
        {
            (NetworkManager owner, ulong ownerId) = Connect();
            (NetworkManager other, ulong otherId) = Connect();
            GeneratedRpcBehaviour onServer = SpawnOwnedBy(prefab, ownerId);

            LogAssert.Expect(LogType.Warning, new Regex($"dropped server RPC 0x20000031 to object {onServer.NetworkObject.ObjectId} from peer {otherId}, which does not own"));
            LogAssert.Expect(LogType.Warning, new Regex($"dropped server RPC 0x20000034 to object {onServer.NetworkObject.ObjectId} from peer {otherId}, which does not own"));
            Assert.AreEqual(SendResult.Queued, Remote(owner, onServer).RequestFire(3));
            Remote(owner, onServer).RequestJump();
            Remote(other, onServer).RequestFire(4);
            Remote(other, onServer).RequestOpen(5);
            Remote(other, onServer).RequestJump();
            Remote(other, onServer).RequestWave();
            RunFrames(3);

            Assert.AreEqual(new[] { $"Fire {ownerId} 3", $"Jump {ownerId}", "Open 5", "Wave" }, onServer.Calls);
        }

        [Test]
        public void ClientRpcsFromAttributesAndAHandWrittenRegistrationReachTheClients()
        {
            (NetworkManager first, ulong firstId) = Connect();
            (NetworkManager second, ulong _) = Connect();
            GeneratedRpcBehaviour onServer = SpawnOwnedBy(prefab, 0);

            Assert.AreEqual(SendResult.Queued, onServer.AnnounceTo(firstId, 7));
            Assert.AreEqual(2, onServer.PingObservers());
            Assert.AreEqual(2, onServer.ShoutObservers());
            RunFrames(3);

            Assert.AreEqual(new[] { "Announce 7", "Ping", "Shout" }, Remote(first, onServer).Calls);
            Assert.AreEqual(new[] { "Ping", "Shout" }, Remote(second, onServer).Calls);
            Assert.IsEmpty(onServer.Calls);
        }

        [Test]
        public void SubclassRegistersItsOwnAndItsBaseClassRpcs()
        {
            (NetworkManager owner, ulong ownerId) = Connect();
            GeneratedRpcBehaviour onServer = SpawnOwnedBy(derivedPrefab, ownerId);
            var onServerDerived = (DerivedGeneratedRpcBehaviour)onServer;

            Remote(owner, onServer).RequestFire(9);
            onServerDerived.CheerObservers();
            onServer.PingObservers();
            RunFrames(3);

            Assert.AreEqual(new[] { $"Fire {ownerId} 9" }, onServer.Calls);
            Assert.AreEqual(new[] { "Cheer", "Ping" }, Remote(owner, onServer).Calls);
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

        private GeneratedRpcBehaviour SpawnOwnedBy(NetworkObject source, ulong ownerId)
        {
            NetworkObject instance = UnityEngine.Object.Instantiate(source);
            server.ServerManager.Spawn(instance, ownerId);
            RunFrames(3);
            return instance.GetComponent<GeneratedRpcBehaviour>();
        }

        private static GeneratedRpcBehaviour Remote(NetworkManager client, GeneratedRpcBehaviour onServer) =>
            client.ClientManager.Spawned[onServer.NetworkObject.ObjectId].GetComponent<GeneratedRpcBehaviour>();

        private NetworkManager CreateManager(NetworkTransport transport)
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("transport").objectReferenceValue = transport;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var ids = new[]
            {
                GeneratedValueNetAdapter.Id,
                GeneratedValueWideAdapter.Id,
                GeneratedNoticeNetAdapter.Id,
                GeneratedRpcBehaviour.JumpId,
                GeneratedRpcBehaviour.WaveId,
                GeneratedRpcBehaviour.PingId,
                GeneratedRpcBehaviour.ShoutId,
                DerivedGeneratedRpcBehaviour.CheerId,
            };
            var schemas = new MessageSchema[ids.Length];
            for (int index = 0; index < ids.Length; index++)
            {
                ulong fingerprint = 0xF031UL + (ulong)index;
                schemas[index] = new MessageSchema(ids[index], fingerprint, new[] { fingerprint });
            }

            FomoxaRegistry registry = TestObjects.Registry(schemas);
            foreach (uint id in ids)
            {
                registry.Channels.Set(id, Channel.ReliableOrdered);
            }

            GeneratedRpcBehaviour.Declare(registry.Rpcs);
            DerivedGeneratedRpcBehaviour.DeclareDerived(registry.Rpcs);
            manager.Registry = registry;
            manager.Initialize();
            manager.ServerManager.FindSceneObjects = () => new List<NetworkObject>();
            manager.ClientManager.FindSceneObjects = () => new List<NetworkObject>();
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
