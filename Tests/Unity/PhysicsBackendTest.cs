using System;
using System.Collections.Generic;
using BundleFixture;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Simulation;
using Fomoxa.Unity.Tests.Support;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Fomoxa.Unity.Tests
{
    public sealed class PhysicsBackendTest
    {
        private const double FrameSeconds = 1.0 / 60;

        private readonly List<GameObject> created = new List<GameObject>();
        private readonly List<NetworkManager> managers = new List<NetworkManager>();
        private InMemoryNetworkTransport network;
        private TimeSpan now;

        [SetUp]
        public void CreateNetwork()
        {
            now = TimeSpan.FromSeconds(1);
            network = new GameObject("InMemoryNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(network.gameObject);
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
        }

        [Test]
        public void AClientWithAnotherPhysicsBackendStopsWithoutRetrying()
        {
            NetworkManager server = CreateManager(PhysicsBackend.Rapier);
            server.ServerManager.StartConnection(1);
            NetworkManager client = CreateManager(PhysicsBackend.Rigidbody);
            var stops = new List<ConnectionStateArgs>();
            client.ClientManager.OnClientConnectionState += args =>
            {
                if (args.State == ConnectionState.Stopped)
                {
                    stops.Add(args);
                }
            };

            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(60);

            Assert.AreEqual(1, stops.Count);
            Assert.AreEqual(StopReason.PhysicsBackendMismatch, stops[0].Reason);
            Assert.IsFalse(stops[0].WillRetry);
            Assert.AreEqual(ConnectionState.Stopped, client.ClientManager.State);
        }

        [Test]
        public void TheSameBackendConnectsAndTheHostUsesItsOwnBackend()
        {
            NetworkManager host = CreateManager(PhysicsBackend.Rapier);
            host.ServerManager.StartConnection(1);
            host.ClientManager.StartConnection("unused.invalid", 1);
            NetworkManager client = CreateManager(PhysicsBackend.Rapier);
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(30);

            Assert.AreEqual(ConnectionState.Started, host.ClientManager.State);
            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
        }

        private NetworkManager CreateManager(PhysicsBackend backend)
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("transport").objectReferenceValue = network;
            serialized.FindProperty("physicsBackend").intValue = (int)backend;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            manager.Registry = TestObjects.Registry();
            manager.Initialize();
            manager.FindServerSceneObjects = () => new List<NetworkObject>();
            manager.FindClientSceneObjects = () => new List<NetworkObject>();
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
