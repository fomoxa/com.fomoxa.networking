using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Transports;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Fomoxa.Unity.Tests
{
    public sealed class MultiNetworkTransportTest
    {
        private const uint GreetingId = 0x2000_0001;
        private const double FrameSeconds = 1.0 / 30;

        private readonly List<NetworkManager> managers = new List<NetworkManager>();
        private readonly List<GameObject> created = new List<GameObject>();

        [TearDown]
        public void DestroyAll()
        {
            NetworkTransport.BrowserOverride = null;
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
        public void AServerTakesAUdpClientAndAWebSocketClientEachWithItsOwnBudget()
        {
            ushort webSocketPort = FreePort();
            MultiNetworkTransport multi = Multi((Component<UdpNetworkTransport>(), 0), (Component<WebSocketNetworkTransport>(), webSocketPort));
            NetworkManager server = Create(multi);
            var received = new List<(ulong PeerId, byte Value)>();
            server.ServerManager.Dispatcher.Register(GreetingId, (peerId, payload) => received.Add((peerId, payload.Span[0])));
            var peers = new List<ulong>();
            server.ServerManager.OnRemoteConnectionState += args =>
            {
                if (args.State == ConnectionState.Started)
                {
                    peers.Add(args.PeerId);
                }
            };
            server.ServerManager.StartConnection(0);

            NetworkManager udpClient = Create(Component<UdpNetworkTransport>());
            udpClient.ClientManager.StartConnection("127.0.0.1", server.ServerManager.Port);
            RunUntil(() => udpClient.ClientManager.State == ConnectionState.Started && peers.Count == 1);
            NetworkManager webSocketClient = Create(Component<WebSocketNetworkTransport>());
            webSocketClient.ClientManager.StartConnection("ws://127.0.0.1:" + webSocketPort + "/", 0);
            RunUntil(() => webSocketClient.ClientManager.State == ConnectionState.Started && peers.Count == 2);

            udpClient.ClientManager.Send(GreetingId, new byte[] { 1 });
            webSocketClient.ClientManager.Send(GreetingId, new byte[] { 2 });
            RunUntil(() => received.Count == 2);

            CollectionAssert.AreEquivalent(new[] { (peers[0], (byte)1), (peers[1], (byte)2) }, received);
            Assert.AreEqual(1200, server.ServerManager.FrameBudgetOf(peers[0]));
            Assert.AreEqual(FomoxaWire.MaxDataFrameSize, server.ServerManager.FrameBudgetOf(peers[1]));
        }

        [Test]
        public void TheLocalClientOfAHostUsesTheBudgetOfTheFirstTransport()
        {
            NetworkManager host = Create(Multi((Component<UdpNetworkTransport>(), 0), (Component<WebSocketNetworkTransport>(), FreePort())));
            ulong local = 0;
            host.ServerManager.OnRemoteConnectionState += args => local = args.PeerId;
            host.ServerManager.StartConnection(0);
            host.ClientManager.StartConnection("unused.invalid", 1);
            RunUntil(() => host.ClientManager.State == ConnectionState.Started);

            Assert.AreEqual(1200, host.ServerManager.FrameBudgetOf(local));
        }

        [Test]
        public void InABrowserOnlyTheTransportsThatRunThereAreKept()
        {
            NetworkTransport.BrowserOverride = true;

            var factory = (MultiTransportFactory)Multi((Component<UdpNetworkTransport>(), 0), (Component<WebSocketNetworkTransport>(), 9000)).CreateFactory();
            Assert.AreEqual(1, factory.Count);
            Assert.AreEqual(FomoxaWire.MaxDataFrameSize, factory.FrameBudget);
            Assert.Throws<PlatformNotSupportedException>(() => Multi((Component<UdpNetworkTransport>(), 0)).CreateFactory());

            NetworkTransport.BrowserOverride = false;
            Assert.Throws<InvalidOperationException>(() => Multi().CreateFactory());
            Assert.Throws<InvalidOperationException>(() => Multi((Component<UdpNetworkTransport>(), 70000)).CreateFactory());
            Assert.AreEqual(2, ((MultiTransportFactory)Multi((Component<UdpNetworkTransport>(), 0), (Component<TcpNetworkTransport>(), 1)).CreateFactory()).Count);
        }

        private MultiNetworkTransport Multi(params (NetworkTransport Transport, int Port)[] entries)
        {
            var multi = Component<MultiNetworkTransport>();
            foreach ((NetworkTransport transport, int port) in entries)
            {
                multi.Add(transport, port);
            }

            return multi;
        }

        private T Component<T>()
            where T : NetworkTransport
        {
            var component = new GameObject(typeof(T).Name).AddComponent<T>();
            created.Add(component.gameObject);
            return component;
        }

        private NetworkManager Create(NetworkTransport transport)
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            created.Add(manager.gameObject);
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("transport").objectReferenceValue = transport;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            manager.Registry = TestObjects.Registry(new MessageSchema(GreetingId, 0xF00D, new ulong[] { 0xF00D }));
            manager.Initialize();
            managers.Add(manager);
            return manager;
        }

        private void RunUntil(Func<bool> done)
        {
            var clock = Stopwatch.StartNew();
            while (!done())
            {
                Assert.Less(clock.Elapsed, TimeSpan.FromSeconds(10), "timed out");
                foreach (NetworkManager manager in managers)
                {
                    manager.RunFrameStart(FrameSeconds, MonotonicClock.Now);
                }

                foreach (NetworkManager manager in managers)
                {
                    manager.RunFrameEnd();
                }

                Thread.Sleep(1);
            }
        }

        private static ushort FreePort()
        {
            using var probe = new TcpListenerTransport(new IPEndPoint(IPAddress.Any, 0));
            return (ushort)probe.LocalEndPoint.Port;
        }
    }
}
