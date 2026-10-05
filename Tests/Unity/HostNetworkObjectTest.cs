using System;
using System.Collections.Generic;
using BundleFixture;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Sessions;
using Fomoxa.Unity.Tests.Support;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Fomoxa.Unity.Tests
{
    public sealed class HostNetworkObjectTest
    {
        private const uint PrefabId = 0xA1;
        private const double FrameSeconds = 1.0 / 30;

        private NetworkManager host;
        private NetworkObject prefab;
        private List<ObjectMismatchArgs> mismatches;
        private TimeSpan now;

        [SetUp]
        public void CreateHost()
        {
            RecordingBehaviour.Clear();
            now = TimeSpan.Zero;
            host = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            var serialized = new SerializedObject(host);
            serialized.FindProperty("transport").objectReferenceValue = host.gameObject.AddComponent<InMemoryNetworkTransport>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            host.Registry = TestObjects.Registry();
            host.Initialize();
            prefab = TestPrefabs.Create("Prefab", PrefabId);
            host.Prefabs.Register(prefab);
            mismatches = new List<ObjectMismatchArgs>();
            host.ClientManager.OnObjectMismatch += mismatches.Add;
            host.ServerManager.StartConnection(1);
            host.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(20);
            Assert.AreEqual(ConnectionState.Started, host.ClientManager.State);
        }

        [TearDown]
        public void DestroyHost()
        {
            host.ClientManager.StopConnection();
            host.ServerManager.StopConnection();
            UnityEngine.Object.DestroyImmediate(host.gameObject);
            TestPrefabs.DestroyAllInScene();
            RecordingBehaviour.Clear();
        }

        [Test]
        public void HostClientUsesTheServerInstance()
        {
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);

            host.ServerManager.Spawn(instance);
            RunFrames(3);

            Assert.AreSame(instance, host.ClientManager.Spawned[instance.ObjectId]);
            Assert.AreEqual(new[] { "1 StartServer", "1 StartClient" }, RecordingBehaviour.Log);
            Assert.AreEqual(2, TestPrefabs.CountInScene());
        }

        [Test]
        public void OwnerChangeCallsEachCallbackOnce()
        {
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            host.ServerManager.Spawn(instance);
            RunFrames(3);
            RecordingBehaviour.Log.Clear();
            ulong localPeerId = host.ClientManager.Objects.LocalPeerId;

            host.ServerManager.Objects.ChangeOwner(instance.ObjectId, localPeerId);

            Assert.AreEqual(localPeerId, instance.OwnerId);
            Assert.IsFalse(instance.IsOwner);
            Assert.AreEqual(new[] { "1 OwnerChangedServer 0" }, RecordingBehaviour.Log);
            RunFrames(3);
            Assert.IsTrue(instance.IsOwner);
            Assert.AreEqual(new[] { "1 OwnerChangedServer 0", "1 OwnerChangedClient 0" }, RecordingBehaviour.Log);
        }

        [Test]
        public void DespawnStopsTheClientSideBeforeTheServerSide()
        {
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            host.ServerManager.Spawn(instance);
            RunFrames(3);
            RecordingBehaviour.Log.Clear();

            Assert.IsTrue(host.ServerManager.Despawn(instance));

            Assert.AreEqual(new[] { "1 StopClient", "1 StopServer" }, RecordingBehaviour.Log);
            Assert.AreEqual(0, host.ClientManager.Spawned.Count);
            Assert.IsTrue(instance == null);
            RunFrames(3);
            Assert.AreEqual(new[] { "1 StopClient", "1 StopServer" }, RecordingBehaviour.Log);
            Assert.AreEqual(0, host.ClientManager.Objects.Count);
            Assert.IsEmpty(mismatches);
            Assert.AreEqual(ConnectionState.Started, host.ClientManager.State);
        }

        [Test]
        public void SpawnAndDespawnInOneTickKeepTheHostClientRunning()
        {
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);

            host.ServerManager.Spawn(instance);
            host.ServerManager.Despawn(instance);
            RunFrames(3);

            Assert.AreEqual(new[] { "1 StartServer", "1 StopServer" }, RecordingBehaviour.Log);
            Assert.AreEqual(0, host.ClientManager.Spawned.Count);
            Assert.AreEqual(0, host.ClientManager.Objects.Count);
            Assert.IsEmpty(mismatches);
            Assert.AreEqual(ConnectionState.Started, host.ClientManager.State);
        }

        [Test]
        public void LocalClientStopKeepsTheServerInstance()
        {
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            host.ServerManager.Spawn(instance);
            RunFrames(3);
            RecordingBehaviour.Log.Clear();

            host.ClientManager.StopConnection();

            Assert.AreEqual(new[] { "1 StopClient" }, RecordingBehaviour.Log);
            Assert.AreEqual(0, host.ClientManager.Spawned.Count);
            Assert.IsFalse(instance == null);
            Assert.IsTrue(instance.IsSpawned);
            Assert.AreSame(instance, host.ServerManager.Spawned[instance.ObjectId]);
        }

        [Test]
        public void ServerStopEndsTheSharedInstanceOnBothSidesOnce()
        {
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            host.ServerManager.Spawn(instance);
            RunFrames(3);
            RecordingBehaviour.Log.Clear();

            host.ServerManager.StopConnection();
            RunFrames(3);

            Assert.AreEqual(new[] { "1 StopClient", "1 StopServer" }, RecordingBehaviour.Log);
            Assert.IsTrue(instance == null);
            Assert.AreEqual(0, host.ClientManager.Spawned.Count);
        }

        private void RunFrames(int count)
        {
            for (int frame = 0; frame < count; frame++)
            {
                now += TimeSpan.FromSeconds(FrameSeconds);
                host.RunFrameStart(FrameSeconds, now);
                host.RunFrameEnd();
            }
        }
    }
}
