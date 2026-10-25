using System;
using System.Collections.Generic;
using BundleFixture;
using Fomoxa.Networking;
using Fomoxa.Networking.Sessions;
using Fomoxa.Unity.Tests.Support;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Fomoxa.Unity.Tests
{
    public sealed class CollectionStateTest
    {
        private const uint PrefabId = 0xC5;
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
            now = TimeSpan.Zero;
            network = new GameObject("InMemoryNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(network.gameObject);
            prefab = TestPrefabs.Create("Prefab", PrefabId);
            prefab.gameObject.AddComponent<InventoryBehaviour>();
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
        public void ContentAndOperationsReachTheClientOfANetworkObject()
        {
            NetworkManager client = Connect();
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            InventoryBehaviour onServer = instance.GetComponent<InventoryBehaviour>();
            onServer.Items.Add(new CounterState { Value = 1 });
            onServer.Scores["a"] = 1;
            onServer.Tags.Add(7);
            server.ServerManager.Spawn(instance);
            RunFrames(3);
            InventoryBehaviour onClient = Remote(client, onServer);

            Assert.AreEqual(onServer.Describe(), onClient.StartedWith);
            CollectionAssert.IsEmpty(onClient.Changes);

            onServer.Items.Add(new CounterState { Value = 2 });
            onServer.Items.Modify(0, item => item.Value = 5);
            onServer.Scores.Remove("a");
            onServer.Tags.Add(8);
            RunFrames(3);

            Assert.AreEqual(onServer.Describe(), onClient.Describe());
            CollectionAssert.AreEqual(new[]
            {
                "scores Remove a 1 0",
                "items Add 1  2",
                "items Set 0 1 5",
                "tags Add 0 8",
            }, onClient.Changes);
        }

        [Test]
        public void ACollectionChangedOnTheClientThrows()
        {
            NetworkManager client = Connect();
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            InventoryBehaviour onServer = instance.GetComponent<InventoryBehaviour>();
            onServer.Items.Add(new CounterState { Value = 1 });
            server.ServerManager.Spawn(instance);
            RunFrames(3);
            InventoryBehaviour onClient = Remote(client, onServer);

            Assert.Throws<InvalidOperationException>(() => onClient.Items.Add(new CounterState()));
            Assert.Throws<InvalidOperationException>(() => onClient.Items.Modify(0, item => item.Value = 9));
            Assert.Throws<InvalidOperationException>(() => onClient.Scores["a"] = 1);
            Assert.Throws<InvalidOperationException>(() => onClient.Tags.Add(1));
            Assert.AreEqual(onServer.Describe(), onClient.Describe());
        }

        [Test]
        public void TheHostClientReportsChangesFromItsOwnCopy()
        {
            var hostNetwork = new GameObject("HostNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(hostNetwork.gameObject);
            NetworkManager host = CreateManager(hostNetwork);
            host.ServerManager.StartConnection(2);
            host.ClientManager.StartConnection("unused.invalid", 2);
            RunFrames(20);
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab);
            InventoryBehaviour shared = instance.GetComponent<InventoryBehaviour>();
            shared.Items.Add(new CounterState { Value = 1 });
            host.ServerManager.Spawn(instance);
            RunFrames(3);

            shared.Items[0] = new CounterState { Value = 3 };
            shared.Items.Add(new CounterState { Value = 4 });
            RunFrames(3);

            Assert.AreEqual("items [3,4] scores [] tags []", shared.Describe());
            CollectionAssert.AreEqual(new[] { "items Set 0 1 3", "items Add 1  4" }, shared.Changes);
        }

        private NetworkManager Connect()
        {
            NetworkManager client = CreateManager(network);
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(20);
            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
            return client;
        }

        private static InventoryBehaviour Remote(NetworkManager client, InventoryBehaviour onServer) =>
            ((NetworkObject)client.ClientManager.Spawned[onServer.NetworkObject.ObjectId]).GetComponent<InventoryBehaviour>();

        private NetworkManager CreateManager(NetworkTransport transport)
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("transport").objectReferenceValue = transport;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            manager.Registry = TestObjects.Registry();
            manager.Initialize();
            manager.FindServerSceneObjects = () => new List<NetworkObject>();
            manager.FindClientSceneObjects = () => new List<NetworkObject>();
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
