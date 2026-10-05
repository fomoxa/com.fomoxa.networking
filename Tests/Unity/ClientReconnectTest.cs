using System;
using System.Collections.Generic;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Networking.Sessions;
using Fomoxa.Unity.Tests.Support;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Fomoxa.Unity.Tests
{
    public sealed class ClientReconnectTest
    {
        private const uint GreetingId = 0x2000_0001;
        private const double FrameSeconds = 1.0 / 30;

        private readonly List<GameObject> created = new List<GameObject>();
        private TimeSpan now;

        private NetworkManager Create(NetworkTransport transport)
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("transport").objectReferenceValue = transport;
            serialized.FindProperty("reconnectIntervalSeconds").floatValue = 0.1f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            manager.Registry = TestObjects.Registry(new MessageSchema(GreetingId, 0xF00D, new ulong[] { 0xF00D }));
            manager.Initialize();
            created.Add(manager.gameObject);
            return manager;
        }

        private void RunFrames(int count, params NetworkManager[] managers)
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

        private static List<ConnectionStateArgs> Record(NetworkManager manager)
        {
            var states = new List<ConnectionStateArgs>();
            manager.ClientManager.OnClientConnectionState += states.Add;
            return states;
        }

        [SetUp]
        public void ResetClock()
        {
            now = TimeSpan.Zero;
        }

        [TearDown]
        public void DestroyManagers()
        {
            foreach (GameObject gameObject in created)
            {
                var manager = gameObject.GetComponent<NetworkManager>();
                if (manager != null)
                {
                    manager.ClientManager.StopConnection();
                    manager.ServerManager.StopConnection();
                }

                UnityEngine.Object.DestroyImmediate(gameObject);
            }

            created.Clear();
        }

        [Test]
        public void RemoteClientReconnectsWhenTheServerRestarts()
        {
            var network = new GameObject("InMemoryNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(network.gameObject);
            NetworkManager server = Create(network);
            NetworkManager client = Create(network);
            List<ConnectionStateArgs> states = Record(client);
            server.ServerManager.StartConnection(1);
            client.ClientManager.StartConnection("in-memory", 1);
            RunFrames(20, server, client);
            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);

            server.ServerManager.StopConnection();
            RunFrames(20, server, client);
            ConnectionStateArgs dropped = states.Find(args => args.State == ConnectionState.Stopped);
            Assert.IsTrue(dropped.WillRetry);

            server.ServerManager.StartConnection(1);
            for (int frame = 0; frame < 600 && client.ClientManager.State != ConnectionState.Started; frame++)
            {
                RunFrames(1, server, client);
            }

            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
            Assert.AreEqual(1, server.ServerManager.PeerCount);
            Assert.Greater(network.ConnectorsCreated, 1);
        }

        [Test]
        public void HostClientIsNotRetriedWhenItsServerStops()
        {
            NetworkManager host = Create(null);
            List<ConnectionStateArgs> states = Record(host);
            host.ServerManager.StartConnection(0);
            host.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(20, host);

            host.ServerManager.StopConnection();
            RunFrames(60, host);

            List<ConnectionStateArgs> stops = states.FindAll(args => args.State == ConnectionState.Stopped);
            Assert.AreEqual(1, stops.Count);
            Assert.IsFalse(stops[0].WillRetry);
            Assert.AreEqual(ConnectionState.Stopped, host.ClientManager.State);
        }
    }
}
