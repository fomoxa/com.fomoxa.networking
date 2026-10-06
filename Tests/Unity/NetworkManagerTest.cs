using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
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
    public sealed class NetworkManagerTest
    {
        private const uint GreetingId = 0x2000_0001;
        private const double FrameSeconds = 1.0 / 30;

        private readonly List<NetworkManager> created = new List<NetworkManager>();
        private readonly List<GameObject> extraObjects = new List<GameObject>();
        private TimeSpan now;

        private static FomoxaRegistry TestRegistry() =>
            TestObjects.Registry(new MessageSchema(GreetingId, 0xF00D, new ulong[] { 0xF00D }));

        private NetworkManager Create(TimingMode mode = TimingMode.Tick, NetworkTransport transport = null, Action<SerializedObject> configure = null)
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("timingMode").enumValueIndex = (int)mode;
            serialized.FindProperty("transport").objectReferenceValue = transport;
            configure?.Invoke(serialized);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            manager.Registry = TestRegistry();
            manager.Initialize();
            created.Add(manager);
            return manager;
        }

        private NetworkManager CreateHost(TimingMode mode = TimingMode.Tick)
        {
            NetworkManager host = Create(mode);
            host.ServerManager.StartConnection(0);
            host.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(host, 20);
            Assert.AreEqual(ConnectionState.Started, host.ClientManager.State);
            return host;
        }

        private sealed class ThrowingLeaveCodec : IMessageCodec<PeerLeave>
        {
            public uint MessageId => PeerLeaveNetAdapter.Instance.MessageId;

            public ReadOnlyMemory<byte> Encode(PeerLeave value) => throw new InvalidOperationException("leave failed");

            public void Decode(ReadOnlyMemory<byte> payload, ref PeerLeave value) => PeerLeaveNetAdapter.Instance.Decode(payload, ref value);
        }

        private static void RunInRealTime(Func<bool> done, params NetworkManager[] managers)
        {
            var clock = Stopwatch.StartNew();
            while (!done() && clock.Elapsed < TimeSpan.FromSeconds(10))
            {
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

        private void RunFrames(NetworkManager manager, int count, double frameSeconds = FrameSeconds)
        {
            for (int frame = 0; frame < count; frame++)
            {
                now += TimeSpan.FromSeconds(frameSeconds);
                manager.RunFrameStart(frameSeconds, now);
                manager.RunFrameEnd();
            }
        }

        [SetUp]
        public void ResetClock()
        {
            now = TimeSpan.Zero;
        }

        [TearDown]
        public void DestroyManagers()
        {
            foreach (NetworkManager manager in created)
            {
                manager.ClientManager.StopConnection();
                manager.ServerManager.StopConnection();
                UnityEngine.Object.DestroyImmediate(manager.gameObject);
            }

            created.Clear();
            foreach (GameObject extra in extraObjects)
            {
                UnityEngine.Object.DestroyImmediate(extra);
            }

            extraObjects.Clear();
        }

        [Test]
        public void InitializeWithoutASchemaThrows()
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            manager.Registry = new FomoxaRegistry();

            Assert.Throws<InvalidOperationException>(manager.Initialize);
            UnityEngine.Object.DestroyImmediate(manager.gameObject);
        }

        [Test]
        public void InitializeWithoutAnAckCodecThrows()
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            var registry = new FomoxaRegistry();
            registry.SetSchema(new Schema(0xCAFE, new[] { new MessageSchema(GreetingId, 0xF00D, new ulong[] { 0xF00D }) }));
            registry.SetCodec(MessageBundleNetAdapter.Instance);
            manager.Registry = registry;

            Assert.Throws<InvalidOperationException>(manager.Initialize);
            UnityEngine.Object.DestroyImmediate(manager.gameObject);
        }

        [Test]
        public void InitializeWithoutAnObjectCodecThrows()
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            var registry = new FomoxaRegistry();
            registry.SetSchema(new Schema(0xCAFE, new[] { new MessageSchema(GreetingId, 0xF00D, new ulong[] { 0xF00D }) }));
            registry.SetCodec(MessageBundleNetAdapter.Instance);
            registry.SetCodec(ReliableAckNetAdapter.Instance);
            registry.SetCodec(PeerLeaveNetAdapter.Instance);
            manager.Registry = registry;

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(manager.Initialize);
            StringAssert.Contains("LocalPeer", error.Message);
            UnityEngine.Object.DestroyImmediate(manager.gameObject);
        }

        [Test]
        public void InitializeWithObjectModelsOffTheReliableChannelThrows()
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            FomoxaRegistry registry = TestObjects.Registry();
            registry.Channels.Set(TestObjects.SpawnId, Channel.Unreliable);
            manager.Registry = registry;

            Assert.Throws<ArgumentException>(manager.Initialize);
            UnityEngine.Object.DestroyImmediate(manager.gameObject);
        }

        [Test]
        public void InitializeWithoutABundleCodecThrows()
        {
            var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            var registry = new FomoxaRegistry();
            registry.SetSchema(new Schema(0xCAFE, new[] { new MessageSchema(GreetingId, 0xF00D, new ulong[] { 0xF00D }) }));
            manager.Registry = registry;

            Assert.Throws<InvalidOperationException>(manager.Initialize);
            UnityEngine.Object.DestroyImmediate(manager.gameObject);
        }

        [Test]
        public void HostClientConnectsOverLoopbackAndBothSidesReachStarted()
        {
            NetworkManager host = Create();
            var serverStates = new List<ServerState>();
            var peerStates = new List<ConnectionState>();
            var clientStates = new List<ConnectionState>();
            host.ServerManager.OnServerConnectionState += args => serverStates.Add(args.State);
            host.ServerManager.OnRemoteConnectionState += args => peerStates.Add(args.State);
            host.ClientManager.OnClientConnectionState += args => clientStates.Add(args.State);

            host.ServerManager.StartConnection(0);
            host.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(host, 20);

            Assert.AreEqual(new[] { ServerState.Starting, ServerState.Started }, serverStates.ToArray());
            Assert.AreEqual(new[] { ConnectionState.Starting, ConnectionState.Started }, peerStates.ToArray());
            Assert.AreEqual(new[] { ConnectionState.Starting, ConnectionState.Started }, clientStates.ToArray());
            Assert.AreEqual(1, host.ServerManager.PeerCount);
        }

        [Test]
        public void EachSideDispatchesTheSameMessageIdWithItsOwnHandler()
        {
            NetworkManager host = Create();
            var serverReceived = new List<byte>();
            var clientReceived = new List<byte>();
            var peerIds = new List<ulong>();
            host.ServerManager.Dispatcher.Register(GreetingId, (peerId, payload) => serverReceived.Add(payload.Span[0]));
            host.ClientManager.Dispatcher.Register(GreetingId, (peerId, payload) => clientReceived.Add(payload.Span[0]));
            host.ServerManager.OnRemoteConnectionState += args => peerIds.Add(args.PeerId);
            host.ServerManager.StartConnection(0);
            host.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(host, 20);

            Assert.AreEqual(SendResult.Queued, host.ClientManager.Send(GreetingId, new byte[] { 1 }));
            Assert.AreEqual(SendResult.Queued, host.ServerManager.Send(peerIds[0], GreetingId, new byte[] { 2 }));
            RunFrames(host, 3);

            Assert.AreEqual(new byte[] { 1 }, serverReceived.ToArray());
            Assert.AreEqual(new byte[] { 2 }, clientReceived.ToArray());
        }

        [Test]
        public void WithoutAnAssignedTransportTheManagerAddsUdp()
        {
            NetworkManager manager = Create();

            Assert.IsInstanceOf<UdpNetworkTransport>(manager.TransportManager.Transport);
            Assert.AreSame(manager.TransportManager.Transport, manager.GetComponent<UdpNetworkTransport>());
        }

        [Test]
        public void TransportDefinedOutsideThePackageCarriesARemoteClient()
        {
            var network = new GameObject("InMemoryNetwork").AddComponent<InMemoryNetworkTransport>();
            extraObjects.Add(network.gameObject);
            NetworkManager server = Create(transport: network);
            NetworkManager client = Create(transport: network);
            server.ServerManager.StartConnection(1);

            client.ClientManager.StartConnection("in-memory", 1);
            for (int frame = 0; frame < 20 && client.ClientManager.State != ConnectionState.Started; frame++)
            {
                now += TimeSpan.FromSeconds(FrameSeconds);
                server.RunFrameStart(FrameSeconds, now);
                client.RunFrameStart(FrameSeconds, now);
                server.RunFrameEnd();
                client.RunFrameEnd();
            }

            Assert.AreEqual(1, server.ServerManager.Port);
            Assert.AreEqual(1, network.ConnectorsCreated);
            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
            Assert.AreEqual(1, server.ServerManager.PeerCount);
        }

        [Test]
        public void RemoteClientThatStopsOverUdpLeavesTheServerAtOnce()
        {
            var udp = new GameObject("Udp").AddComponent<UdpNetworkTransport>();
            extraObjects.Add(udp.gameObject);
            NetworkManager server = Create(transport: udp);
            NetworkManager client = Create(transport: udp);
            var ended = new List<ConnectionStateArgs>();
            server.ServerManager.OnRemoteConnectionState += args =>
            {
                if (args.State == ConnectionState.Stopped)
                {
                    ended.Add(args);
                }
            };
            server.ServerManager.StartConnection(0);
            client.ClientManager.StartConnection("127.0.0.1", server.ServerManager.Port);
            RunInRealTime(() => client.ClientManager.State == ConnectionState.Started && server.ServerManager.PeerCount == 1, server, client);
            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);

            client.ClientManager.StopConnection();
            var clock = Stopwatch.StartNew();
            RunInRealTime(() => server.ServerManager.PeerCount == 0, server);

            Assert.AreEqual(0, server.ServerManager.PeerCount);
            Assert.Less(clock.Elapsed, TimeSpan.FromSeconds(1));
            Assert.AreEqual(1, ended.Count);
            Assert.AreEqual(StopReason.PeerClosed, ended[0].Reason);
        }

        [Test]
        public void AClientThatFailsToLeaveStillLetsTheHostServerStop()
        {
            var host = new GameObject("NetworkManager").AddComponent<NetworkManager>();
            FomoxaRegistry registry = TestRegistry();
            registry.SetCodec(new ThrowingLeaveCodec());
            host.Registry = registry;
            host.Initialize();
            created.Add(host);
            host.ServerManager.StartConnection(0);
            host.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(host, 20);
            Assert.AreEqual(ConnectionState.Started, host.ClientManager.State);
            Assert.AreEqual(ServerState.Started, host.ServerManager.State);

            var stop = typeof(NetworkManager).GetMethod("StopConnections", BindingFlags.NonPublic | BindingFlags.Instance);
            TargetInvocationException error = Assert.Throws<TargetInvocationException>(() => stop.Invoke(host, null));

            Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);
            Assert.AreEqual(ConnectionState.Stopped, host.ClientManager.State);
            Assert.AreEqual(ServerState.Stopped, host.ServerManager.State);
        }

        [Test]
        public void HeartbeatSettingsOfTheManagerEndASilentSession()
        {
            var network = new GameObject("InMemoryNetwork").AddComponent<InMemoryNetworkTransport>();
            extraObjects.Add(network.gameObject);
            NetworkManager server = Create(transport: network);
            NetworkManager quick = Create(transport: network, configure: serialized =>
            {
                serialized.FindProperty("heartbeatIntervalSeconds").floatValue = 0.1f;
                serialized.FindProperty("heartbeatTimeoutSeconds").floatValue = 0.2f;
            });
            NetworkManager patient = Create(transport: network);
            var quickStates = new List<ConnectionStateArgs>();
            var patientStates = new List<ConnectionStateArgs>();
            quick.ClientManager.OnClientConnectionState += quickStates.Add;
            patient.ClientManager.OnClientConnectionState += patientStates.Add;
            server.ServerManager.StartConnection(1);
            quick.ClientManager.StartConnection("in-memory", 1);
            patient.ClientManager.StartConnection("in-memory", 1);
            for (int frame = 0; frame < 20; frame++)
            {
                now += TimeSpan.FromSeconds(FrameSeconds);
                server.RunFrameStart(FrameSeconds, now);
                quick.RunFrameStart(FrameSeconds, now);
                patient.RunFrameStart(FrameSeconds, now);
                server.RunFrameEnd();
                quick.RunFrameEnd();
                patient.RunFrameEnd();
            }

            Assert.AreEqual(ConnectionState.Started, quick.ClientManager.State);
            Assert.AreEqual(ConnectionState.Started, patient.ClientManager.State);
            for (int frame = 0; frame < 30; frame++)
            {
                now += TimeSpan.FromSeconds(FrameSeconds);
                quick.RunFrameStart(FrameSeconds, now);
                patient.RunFrameStart(FrameSeconds, now);
                quick.RunFrameEnd();
                patient.RunFrameEnd();
            }

            Assert.IsTrue(quickStates.Exists(args => args.State == ConnectionState.Stopped && args.Reason == StopReason.Timeout));
            Assert.IsFalse(patientStates.Exists(args => args.State == ConnectionState.Stopped));
        }

        [Test]
        public void RemoteClientConnectsOverTcpWithoutBlocking()
        {
            var tcp = new GameObject("Tcp").AddComponent<TcpNetworkTransport>();
            extraObjects.Add(tcp.gameObject);
            NetworkManager server = Create(transport: tcp);
            NetworkManager client = Create(transport: tcp);
            server.ServerManager.StartConnection(0);
            Assert.AreNotEqual(0, server.ServerManager.Port);

            client.ClientManager.StartConnection("127.0.0.1", server.ServerManager.Port);
            Assert.AreEqual(ConnectionState.Starting, client.ClientManager.State);

            var clock = Stopwatch.StartNew();
            while (client.ClientManager.State != ConnectionState.Started && clock.Elapsed < TimeSpan.FromSeconds(10))
            {
                server.RunFrameStart(FrameSeconds, MonotonicClock.Now);
                client.RunFrameStart(FrameSeconds, MonotonicClock.Now);
                server.RunFrameEnd();
                client.RunFrameEnd();
                Thread.Sleep(1);
            }

            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
            Assert.AreEqual(1, server.ServerManager.PeerCount);
        }

        [Test]
        public void RemoteClientConnectsOverWebSocketAndSendsAMessage()
        {
            var webSocket = new GameObject("WebSocket").AddComponent<WebSocketNetworkTransport>();
            extraObjects.Add(webSocket.gameObject);
            NetworkManager server = Create(transport: webSocket);
            NetworkManager client = Create(transport: webSocket);
            var received = new List<byte>();
            server.ServerManager.Dispatcher.Register(GreetingId, (peerId, payload) => received.AddRange(payload.ToArray()));
            server.ServerManager.StartConnection(0);

            client.ClientManager.StartConnection("ws://127.0.0.1:" + server.ServerManager.Port + "/", 1);
            var clock = Stopwatch.StartNew();
            bool sent = false;
            while (received.Count == 0 && clock.Elapsed < TimeSpan.FromSeconds(10))
            {
                if (!sent && client.ClientManager.State == ConnectionState.Started)
                {
                    sent = client.ClientManager.Send(GreetingId, new byte[] { 7, 8, 9 }) == SendResult.Queued;
                }

                server.RunFrameStart(FrameSeconds, MonotonicClock.Now);
                client.RunFrameStart(FrameSeconds, MonotonicClock.Now);
                server.RunFrameEnd();
                client.RunFrameEnd();
                Thread.Sleep(1);
            }

            Assert.AreEqual(1, server.ServerManager.PeerCount);
            CollectionAssert.AreEqual(new byte[] { 7, 8, 9 }, received);
        }

        [Test]
        public void TickModeReceivesInTheFirstTickOfTheFrameBetweenPreTickAndTick()
        {
            NetworkManager host = CreateHost();
            var order = new List<string>();
            host.ServerManager.Dispatcher.Register(GreetingId, (peerId, payload) => order.Add("message"));
            host.TimeManager.OnPreTick += () => order.Add("pre");
            host.TimeManager.OnTick += () => order.Add("tick");
            host.TimeManager.OnPostTick += () => order.Add("post");
            host.ClientManager.Send(GreetingId, new byte[] { 1 });
            RunFrames(host, 1);
            order.Clear();

            RunFrames(host, 1, 2 * FrameSeconds);

            Assert.AreEqual(new[] { "pre", "message", "tick", "post", "pre", "tick", "post" }, order.ToArray());
        }

        [Test]
        public void FrameLongerThanTheCapRunsAtMostMaxTicksPerFrameAndDropsTheRest()
        {
            NetworkManager manager = Create();
            int ticks = 0;
            manager.TimeManager.OnTick += () => ticks++;

            RunFrames(manager, 1, 2.0);
            Assert.AreEqual(3, ticks);
            Assert.AreEqual(3u, manager.TimeManager.Tick);

            RunFrames(manager, 1);
            Assert.AreEqual(4, ticks);
            Assert.AreEqual(4u, manager.TimeManager.Tick);
        }

        [Test]
        public void FrameShorterThanATickRunsNoTick()
        {
            NetworkManager manager = Create();
            int ticks = 0;
            manager.TimeManager.OnTick += () => ticks++;

            RunFrames(manager, 1, FrameSeconds / 2);
            Assert.AreEqual(0, ticks);

            RunFrames(manager, 1, FrameSeconds / 2);
            Assert.AreEqual(1, ticks);
        }

        [Test]
        public void VariableModeSendsOnlyAtTheEndOfTheFrame()
        {
            NetworkManager host = CreateHost(TimingMode.Variable);
            var received = new List<byte>();
            host.ServerManager.Dispatcher.Register(GreetingId, (peerId, payload) => received.Add(payload.Span[0]));

            host.ClientManager.Send(GreetingId, new byte[] { 5 });
            host.RunFrameStart(FrameSeconds, now);
            Assert.AreEqual(0, received.Count);

            host.RunFrameEnd();
            Assert.AreEqual(0, received.Count);

            host.RunFrameStart(FrameSeconds, now);
            Assert.AreEqual(new byte[] { 5 }, received.ToArray());
            host.RunFrameEnd();
        }

        [Test]
        public void StoppingTheServerStopsTheHostClient()
        {
            NetworkManager host = CreateHost();
            var clientStops = new List<StopReason>();
            host.ClientManager.OnClientConnectionState += args =>
            {
                if (args.State == ConnectionState.Stopped)
                {
                    clientStops.Add(args.Reason);
                }
            };

            host.ServerManager.StopConnection();
            RunFrames(host, 2);

            Assert.AreEqual(ConnectionState.Stopped, host.ClientManager.State);
            Assert.AreEqual(1, clientStops.Count);
        }

        [Test]
        public void HandlerExceptionWithoutASubscriberIsLoggedAndOnlyThatPeerIsDisconnected()
        {
            NetworkManager host = CreateHost();
            host.ServerManager.Dispatcher.Register(GreetingId, (peerId, payload) => throw new InvalidOperationException("handler failed"));
            var peerStops = new List<StopReason>();
            host.ServerManager.OnRemoteConnectionState += args =>
            {
                if (args.State == ConnectionState.Stopped)
                {
                    peerStops.Add(args.Reason);
                }
            };
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: handler failed");

            Assert.AreEqual(SendResult.Queued, host.ClientManager.Send(GreetingId, new byte[] { 1 }));
            RunFrames(host, 3);

            Assert.AreEqual(new[] { StopReason.HandlerException }, peerStops.ToArray());
            Assert.AreEqual(ServerState.Started, host.ServerManager.State);
            Assert.AreEqual(0, host.ServerManager.PeerCount);
        }

        [Test]
        public void HandlerExceptionWithASubscriberGoesToTheSubscriberAndStopsTheClient()
        {
            NetworkManager host = CreateHost();
            host.ClientManager.Dispatcher.Register(GreetingId, (peerId, payload) => throw new InvalidOperationException("handler failed"));
            var exceptions = new List<HandlerExceptionArgs>();
            var clientStops = new List<StopReason>();
            host.ClientManager.OnHandlerException += exceptions.Add;
            host.ClientManager.OnClientConnectionState += args =>
            {
                if (args.State == ConnectionState.Stopped)
                {
                    clientStops.Add(args.Reason);
                }
            };
            host.ServerManager.Broadcast(GreetingId, new byte[] { 1 });
            RunFrames(host, 3);

            Assert.AreEqual(1, exceptions.Count);
            Assert.AreEqual(GreetingId, exceptions[0].MessageId);
            Assert.AreEqual(new[] { StopReason.HandlerException }, clientStops.ToArray());
            Assert.AreEqual(ConnectionState.Stopped, host.ClientManager.State);
        }
    }
}
