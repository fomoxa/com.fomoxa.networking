using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Sessions;
using Fomoxa.Unity.Tests.Support;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Fomoxa.Unity.Tests
{
    public sealed class NetworkTransformTest
    {
        private const uint RootPrefabId = 0xE1;
        private const uint ChildPrefabId = 0xE2;
        private const uint ScaledPrefabId = 0xE3;
        private const double FrameSeconds = 1.0 / 30;

        private readonly List<GameObject> created = new List<GameObject>();
        private readonly List<NetworkManager> managers = new List<NetworkManager>();
        private readonly List<NetworkObject> prefabs = new List<NetworkObject>();
        private InMemoryNetworkTransport network;
        private NetworkManager server;
        private NetworkObject rootPrefab;
        private NetworkObject childPrefab;
        private NetworkObject scaledPrefab;
        private TimeSpan now;

        [SetUp]
        public void CreateServer()
        {
            RecordingBehaviour.Clear();
            now = TimeSpan.Zero;
            network = new GameObject("InMemoryNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(network.gameObject);
            rootPrefab = TestPrefabs.Create("Root", RootPrefabId);
            rootPrefab.gameObject.AddComponent<NetworkTransform>();
            childPrefab = TestPrefabs.Create("Parent", ChildPrefabId);
            var child = new GameObject("Child");
            child.transform.SetParent(childPrefab.transform, false);
            child.AddComponent<NetworkTransform>();
            scaledPrefab = TestPrefabs.Create("Scaled", ScaledPrefabId);
            var scaled = new SerializedObject(scaledPrefab.gameObject.AddComponent<NetworkTransform>());
            scaled.FindProperty("syncScale").boolValue = true;
            scaled.ApplyModifiedPropertiesWithoutUndo();
            prefabs.AddRange(new[] { rootPrefab, childPrefab, scaledPrefab });
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
            prefabs.Clear();
            TestPrefabs.DestroyAllInScene();
            RecordingBehaviour.Clear();
        }

        [Test]
        public void MovementIsSentAndSettlesAtTheExactValue()
        {
            (NetworkManager client, ulong _) = Connect();
            NetworkTransform onServer = Spawn(rootPrefab);
            NetworkTransform onClient = Remote(client, onServer);

            for (int step = 1; step <= 3; step++)
            {
                onServer.transform.localPosition = new Vector3(step, 0, 0);
                onServer.transform.localRotation = Quaternion.Euler(0, 30 * step, 0);
                RunFrames(1);
            }

            RunFrames(5);
            onClient.Advance(1);

            Assert.AreEqual(new Vector3(3, 0, 0), onClient.transform.localPosition);
            Assert.Less(Quaternion.Angle(Quaternion.Euler(0, 90, 0), onClient.transform.localRotation), 0.001f);
            Assert.AreEqual(1, onClient.SnapshotCount);
        }

        [Test]
        public void MovesBelowTheThresholdWaitForTheSettle()
        {
            (NetworkManager client, ulong _) = Connect();
            NetworkTransform onServer = Spawn(rootPrefab);
            NetworkTransform onClient = Remote(client, onServer);

            onServer.transform.localPosition = new Vector3(0.004f, 0, 0);
            RunFrames(1);
            onServer.transform.localPosition = new Vector3(0.008f, 0, 0);
            RunFrames(1);
            float beforeSettle = onClient.transform.localPosition.x;
            RunFrames(3);

            Assert.AreEqual(0f, beforeSettle);
            Assert.AreEqual(0.008f, onClient.transform.localPosition.x);
        }

        [Test]
        public void ClientInterpolatesBetweenSnapshotsAndNeverPassesTheNewest()
        {
            (NetworkManager client, ulong _) = Connect();
            NetworkTransform onServer = Spawn(rootPrefab);
            NetworkTransform onClient = Remote(client, onServer);

            for (int step = 1; step <= 4; step++)
            {
                onServer.transform.localPosition = new Vector3(step, 0, 0);
                RunFrames(1);
            }

            int buffered = onClient.SnapshotCount;
            var seen = new List<float>();
            for (int frame = 0; frame < 12; frame++)
            {
                onClient.Advance(1.0 / 60);
                seen.Add(onClient.transform.localPosition.x);
            }

            Assert.GreaterOrEqual(buffered, 3);
            for (int index = 1; index < seen.Count; index++)
            {
                Assert.LessOrEqual(seen[index - 1], seen[index]);
            }

            Assert.IsTrue(seen.Exists(x => x != Mathf.Round(x)));
            Assert.LessOrEqual(seen[seen.Count - 1], 4f);
            Assert.AreEqual(seen[seen.Count - 1], seen[seen.Count - 2]);
        }

        [Test]
        public void ChildTransformStartsAtTheServerValueForNewAndLatePeers()
        {
            (NetworkManager early, ulong _) = Connect();
            NetworkObject instance = UnityEngine.Object.Instantiate(childPrefab);
            NetworkTransform onServer = instance.GetComponentInChildren<NetworkTransform>();
            onServer.transform.localPosition = new Vector3(5, 0, 0);
            server.ServerManager.Spawn(instance);
            RunFrames(3);
            Vector3 onEarly = Remote(early, onServer).transform.localPosition;

            onServer.transform.localPosition = new Vector3(6, 0, 0);
            RunFrames(5);
            (NetworkManager late, ulong _) = Connect();

            Assert.AreEqual(1, onServer.BehaviourIndex);
            Assert.AreEqual(new Vector3(5, 0, 0), onEarly);
            Assert.AreEqual(new Vector3(6, 0, 0), Remote(late, onServer).transform.localPosition);
        }

        [Test]
        public void ScaleFollowsOnlyWhenSelected()
        {
            (NetworkManager client, ulong _) = Connect();
            NetworkTransform plain = Spawn(rootPrefab);
            NetworkTransform scaled = Spawn(scaledPrefab);

            plain.transform.localScale = new Vector3(2, 2, 2);
            scaled.transform.localScale = new Vector3(3, 3, 3);
            RunFrames(6);

            Assert.AreEqual(Vector3.one, Remote(client, plain).transform.localScale);
            Assert.AreEqual(new Vector3(3, 3, 3), Remote(client, scaled).transform.localScale);
        }

        [Test]
        public void OldTicksAreIgnoredAndAMaskThatDoesNotMatchItsValuesIsDropped()
        {
            (NetworkManager client, ulong clientId) = Connect();
            var dropped = new List<ReceiveDroppedArgs>();
            client.ClientManager.OnReceiveDropped += dropped.Add;
            NetworkTransform onServer = Spawn(rootPrefab);
            NetworkTransform onClient = Remote(client, onServer);
            onServer.transform.localPosition = new Vector3(1, 0, 0);
            RunFrames(5);
            var stale = new TransformUpdate { Tick = onClient.LastTick, Mask = SpawnTransform.PositionBit };
            stale.Values.AddRange(new[] { 9f, 9f, 9f });
            var invalid = new TransformUpdate { Tick = onClient.LastTick + 100, Mask = 8 };

            LogAssert.Expect(LogType.Warning, new Regex("dropped 15 received bytes"));
            server.ServerManager.SendToObject(clientId, TestObjects.TransformUpdateId, onServer.NetworkObject.ObjectId, onServer.BehaviourIndex, TransformUpdateNetAdapter.Instance.Encode(stale).Span);
            server.ServerManager.SendToObject(clientId, TestObjects.TransformUpdateId, onServer.NetworkObject.ObjectId, onServer.BehaviourIndex, TransformUpdateNetAdapter.Instance.Encode(invalid).Span);
            RunFrames(3);
            onClient.Advance(1);

            Assert.AreEqual(new Vector3(1, 0, 0), onClient.transform.localPosition);
            Assert.AreEqual(1, dropped.Count);
            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
        }

        [Test]
        public void ClientMoveIsOverwrittenByTheServer()
        {
            (NetworkManager client, ulong _) = Connect();
            NetworkTransform onServer = Spawn(rootPrefab);
            NetworkTransform onClient = Remote(client, onServer);

            onClient.transform.localPosition = new Vector3(100, 0, 0);
            onServer.transform.localPosition = new Vector3(2, 0, 0);
            RunFrames(6);
            onClient.Advance(1);

            Assert.AreEqual(new Vector3(2, 0, 0), onClient.transform.localPosition);
        }

        [Test]
        public void HostClientIgnoresTransformMessages()
        {
            var hostNetwork = new GameObject("HostNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(hostNetwork.gameObject);
            NetworkManager host = CreateManager(hostNetwork);
            host.ServerManager.StartConnection(2);
            host.ClientManager.StartConnection("unused.invalid", 2);
            RunFrames(20);
            NetworkObject instance = UnityEngine.Object.Instantiate(rootPrefab);
            host.ServerManager.Spawn(instance);
            RunFrames(3);
            NetworkTransform shared = instance.GetComponent<NetworkTransform>();

            shared.transform.localPosition = new Vector3(1, 0, 0);
            RunFrames(1);
            shared.transform.localPosition = new Vector3(2, 0, 0);
            RunFrames(5);

            Assert.AreSame(instance, host.ClientManager.Spawned[instance.ObjectId]);
            Assert.AreEqual(new Vector3(2, 0, 0), shared.transform.localPosition);
            Assert.AreEqual(0u, shared.LastTick);
            Assert.AreEqual(0, shared.SnapshotCount);
        }

        [Test]
        public void TeleportSnapsTheClientWithoutInterpolating()
        {
            (NetworkManager client, ulong _) = Connect();
            NetworkTransform onServer = Spawn(rootPrefab);
            NetworkTransform onClient = Remote(client, onServer);
            for (int step = 1; step <= 3; step++)
            {
                onServer.transform.localPosition = new Vector3(step, 0, 0);
                RunFrames(1);
            }

            onServer.transform.localPosition = new Vector3(50, 0, 0);
            onServer.Teleport();
            RunFrames(1);
            Vector3 afterTeleport = onClient.transform.localPosition;
            int buffered = onClient.SnapshotCount;
            onClient.Advance(1.0 / 60);

            Assert.AreEqual(1, onServer.Generation);
            Assert.AreEqual(new Vector3(50, 0, 0), afterTeleport);
            Assert.AreEqual(1, buffered);
            Assert.AreEqual(new Vector3(50, 0, 0), onClient.transform.localPosition);
        }

        [Test]
        public void FirstUpdateOfANewGenerationSnapsWhenItsSettleIsLost()
        {
            (NetworkManager client, ulong clientId) = Connect();
            NetworkTransform onServer = Spawn(rootPrefab);
            NetworkTransform onClient = Remote(client, onServer);
            onServer.transform.localPosition = new Vector3(1, 0, 0);
            RunFrames(1);
            onServer.transform.localPosition = new Vector3(2, 0, 0);
            RunFrames(1);
            var jumped = new TransformUpdate { Tick = onClient.LastTick + 1, Mask = SpawnTransform.PositionBit, Generation = 1 };
            jumped.Values.AddRange(new[] { 40f, 0f, 0f });

            SendToClient(clientId, TestObjects.TransformUpdateId, onServer, TransformUpdateNetAdapter.Instance.Encode(jumped).Span);
            RunFrames(1);

            Assert.AreEqual(new Vector3(40, 0, 0), onClient.transform.localPosition);
            Assert.AreEqual(1, onClient.SnapshotCount);
        }

        [Test]
        public void LateSettleOfTheSameGenerationIsABarrier()
        {
            (NetworkManager client, ulong clientId) = Connect();
            NetworkTransform onServer = Spawn(rootPrefab);
            NetworkTransform onClient = Remote(client, onServer);
            for (int step = 1; step <= 4; step++)
            {
                onServer.transform.localPosition = new Vector3(step, 0, 0);
                RunFrames(1);
            }

            int before = onClient.SnapshotCount;
            var late = new TransformSettle { Tick = onClient.LastTick - 1, Mask = SpawnTransform.PositionBit };
            late.Values.AddRange(new[] { 100f, 0f, 0f });

            SendToClient(clientId, TestObjects.TransformSettleId, onServer, TransformSettleNetAdapter.Instance.Encode(late).Span);
            onServer.transform.localPosition = new Vector3(5, 0, 0);
            RunFrames(1);
            int after = onClient.SnapshotCount;
            onClient.Advance(0);
            Vector3 atBarrier = onClient.transform.localPosition;
            onClient.Advance(1);

            Assert.GreaterOrEqual(before, 4);
            Assert.AreEqual(3, after);
            Assert.AreEqual(new Vector3(100, 0, 0), atBarrier);
            Assert.AreEqual(new Vector3(5, 0, 0), onClient.transform.localPosition);
        }

        [Test]
        public void LateSettleOfAnOlderGenerationIsDropped()
        {
            (NetworkManager client, ulong clientId) = Connect();
            NetworkTransform onServer = Spawn(rootPrefab);
            NetworkTransform onClient = Remote(client, onServer);
            onServer.transform.localPosition = new Vector3(30, 0, 0);
            onServer.Teleport();
            RunFrames(1);
            onServer.transform.localPosition = new Vector3(31, 0, 0);
            RunFrames(1);
            int before = onClient.SnapshotCount;
            var stale = new TransformSettle { Tick = onClient.LastTick - 1, Mask = SpawnTransform.PositionBit, Generation = 0 };
            stale.Values.AddRange(new[] { 9f, 9f, 9f });

            SendToClient(clientId, TestObjects.TransformSettleId, onServer, TransformSettleNetAdapter.Instance.Encode(stale).Span);
            onServer.transform.localPosition = new Vector3(32, 0, 0);
            RunFrames(1);
            int after = onClient.SnapshotCount;
            onClient.Advance(1);

            Assert.AreEqual(before + 1, after);
            Assert.AreEqual(new Vector3(32, 0, 0), onClient.transform.localPosition);
        }

        [Test]
        public void TeleportOnTheClientOrBeforeSpawnDoesNothing()
        {
            (NetworkManager client, ulong _) = Connect();
            NetworkTransform onServer = Spawn(rootPrefab);
            NetworkTransform onClient = Remote(client, onServer);
            NetworkTransform unspawned = UnityEngine.Object.Instantiate(rootPrefab).GetComponent<NetworkTransform>();
            created.Add(unspawned.gameObject);

            onClient.Teleport();
            unspawned.Teleport();

            Assert.AreEqual(0, onClient.Generation);
            Assert.IsFalse(onClient.SettlePending);
            Assert.AreEqual(0, unspawned.Generation);
            Assert.IsFalse(unspawned.SettlePending);
        }

        [Test]
        public void LatePeerStartsInTheCurrentGenerationAndInterpolatesAfterIt()
        {
            (NetworkManager _, ulong _) = Connect();
            NetworkTransform onServer = Spawn(rootPrefab);
            onServer.transform.localPosition = new Vector3(20, 0, 0);
            onServer.Teleport();
            RunFrames(3);
            (NetworkManager late, ulong _) = Connect();
            NetworkTransform onLate = Remote(late, onServer);

            for (int step = 1; step <= 3; step++)
            {
                onServer.transform.localPosition = new Vector3(20 + step, 0, 0);
                RunFrames(1);
            }

            Assert.AreEqual(1, onServer.Generation);
            Assert.AreEqual(4, onLate.SnapshotCount);
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

        private void SendToClient(ulong clientId, uint messageId, NetworkTransform onServer, ReadOnlySpan<byte> body) =>
            server.ServerManager.SendToObject(clientId, messageId, onServer.NetworkObject.ObjectId, onServer.BehaviourIndex, body);

        private NetworkTransform Spawn(NetworkObject prefab)
        {
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            server.ServerManager.Spawn(instance);
            RunFrames(3);
            return instance.GetComponentInChildren<NetworkTransform>();
        }

        private static NetworkTransform Remote(NetworkManager client, NetworkTransform onServer) =>
            ((NetworkObject)client.ClientManager.Spawned[onServer.NetworkObject.ObjectId]).GetComponentInChildren<NetworkTransform>();

        private NetworkManager CreateManager(NetworkTransport transport)
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("transport").objectReferenceValue = transport;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            manager.Registry = TestObjects.Registry();
            manager.Initialize();
            manager.FindServerSceneObjects = () => new List<NetworkObject>();
            manager.ClientManager.FindSceneObjects = () => new List<NetworkObject>();
            foreach (NetworkObject prefab in prefabs)
            {
                manager.Prefabs.Register(prefab);
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
