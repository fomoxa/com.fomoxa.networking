using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class RemoteClientSessionTest
    {
        private const uint GreetingId = 0x2000_0001;

        private static Schema TestSchema() =>
            new Schema(0xCAFE, new[] { new MessageSchema(GreetingId, 0xF00D, new ulong[] { 0xF00D }) });

        private sealed class DelayedLoopbackConnector : ITransportConnector
        {
            private readonly LoopbackListener listener;
            private int pendingPolls;

            public DelayedLoopbackConnector(LoopbackListener listener, int pendingPolls)
            {
                this.listener = listener;
                this.pendingPolls = pendingPolls;
            }

            public ConnectStatus Poll(out ITransport transport)
            {
                transport = null;
                if (pendingPolls-- > 0)
                {
                    return ConnectStatus.Pending;
                }

                transport = listener.Connect();
                return ConnectStatus.Connected;
            }

            public void Dispose()
            {
            }
        }

        private static void RunUntil(Func<bool> done, params Action<TimeSpan>[] steps)
        {
            var clock = Stopwatch.StartNew();
            while (!done() && clock.Elapsed < TimeSpan.FromSeconds(10))
            {
                foreach (Action<TimeSpan> step in steps)
                {
                    step(MonotonicClock.Now);
                }

                Thread.Sleep(1);
            }
        }

        [Test]
        public void ClientConnectsOverTcpAndItsQueuedMessageArrivesAfterReady()
        {
            var received = new List<byte[]>();
            var serverDispatcher = new MessageDispatcher(TestSchema());
            serverDispatcher.Register(GreetingId, (peerId, payload) => received.Add(payload.ToArray()));
            var server = new ServerSession(TestSchema(), new SessionConfig(), new SessionLimits(), serverDispatcher, TestBundles.Protocol());
            var listener = new TcpListenerTransport(new IPEndPoint(IPAddress.Loopback, 0));
            server.Start(listener);
            var client = new ClientSession(TestSchema(), new SessionConfig(), new SessionLimits(), new MessageDispatcher(TestSchema()), TestBundles.Protocol());
            var clientStates = new List<ConnectionState>();
            client.OnClientConnectionState += args => clientStates.Add(args.State);

            client.Start(new TcpTransportFactory().CreateConnector("127.0.0.1", (ushort)listener.LocalEndPoint.Port));
            Assert.AreEqual(ConnectionState.Starting, client.State);
            Assert.AreEqual(SendResult.Queued, client.Send(GreetingId, new byte[] { 9 }));

            RunUntil(() => received.Count == 1, server.Tick, client.Tick, now => server.Flush(), now => client.Flush());

            Assert.AreEqual(new[] { ConnectionState.Starting, ConnectionState.Started }, clientStates.ToArray());
            Assert.AreEqual(new byte[] { 9 }, received[0]);
            client.Stop();
            server.Stop();
        }

        [Test]
        public void ClientStopOverUdpEndsThePeerAtOnceWithPeerClosed()
        {
            var server = new ServerSession(TestSchema(), new SessionConfig(), new SessionLimits(), new MessageDispatcher(TestSchema()), TestBundles.Protocol());
            var peerStates = new List<ConnectionStateArgs>();
            server.OnRemoteConnectionState += peerStates.Add;
            var factory = new UdpTransportFactory();
            server.Start(factory.CreateListener(0, out ushort port));
            var client = new ClientSession(TestSchema(), new SessionConfig(), new SessionLimits(), new MessageDispatcher(TestSchema()), TestBundles.Protocol());
            client.Start(factory.CreateConnector("127.0.0.1", port));
            RunUntil(() => client.State == ConnectionState.Started && server.PeerCount == 1, server.Tick, client.Tick, now => server.Flush(), now => client.Flush());
            Assert.AreEqual(ConnectionState.Started, client.State);

            client.Stop();
            var clock = Stopwatch.StartNew();
            RunUntil(() => server.PeerCount == 0, server.Tick, now => server.Flush());

            Assert.AreEqual(0, server.PeerCount);
            Assert.Less(clock.Elapsed, TimeSpan.FromSeconds(1));
            ConnectionStateArgs ended = peerStates[peerStates.Count - 1];
            Assert.AreEqual(ConnectionState.Stopped, ended.State);
            Assert.AreEqual(StopReason.PeerClosed, ended.Reason);
            server.Stop();
        }

        [Test]
        public void FailedConnectStopsTheClientWithTransportErrorAndDiscardsItsQueue()
        {
            int closedPort;
            using (var probe = new TcpListenerTransport(new IPEndPoint(IPAddress.Loopback, 0)))
            {
                closedPort = probe.LocalEndPoint.Port;
            }

            var client = new ClientSession(TestSchema(), new SessionConfig(), new SessionLimits(), new MessageDispatcher(TestSchema()), TestBundles.Protocol());
            var stopped = new List<ConnectionStateArgs>();
            client.OnClientConnectionState += args =>
            {
                if (args.State == ConnectionState.Stopped)
                {
                    stopped.Add(args);
                }
            };

            client.Start(new TcpTransportFactory().CreateConnector("127.0.0.1", (ushort)closedPort));
            client.Send(GreetingId, new byte[] { 1 });
            RunUntil(() => stopped.Count == 1, client.Tick, now => client.Flush());

            Assert.AreEqual(1, stopped.Count);
            Assert.AreEqual(StopReason.TransportError, stopped[0].Reason);
            Assert.AreEqual(1, stopped[0].DiscardedMessages);
        }

        [Test]
        public void StopWhileConnectingStopsWithLocalStop()
        {
            using var listener = new TcpListenerTransport(new IPEndPoint(IPAddress.Loopback, 0));
            var client = new ClientSession(TestSchema(), new SessionConfig(), new SessionLimits(), new MessageDispatcher(TestSchema()), TestBundles.Protocol());
            var states = new List<ConnectionStateArgs>();
            client.OnClientConnectionState += states.Add;

            client.Start(new TcpTransportFactory().CreateConnector("127.0.0.1", (ushort)listener.LocalEndPoint.Port));
            client.Stop();

            Assert.AreEqual(ConnectionState.Stopped, client.State);
            Assert.AreEqual(StopReason.LocalStop, states[states.Count - 1].Reason);
            Assert.AreEqual(SendResult.NotConnected, client.Send(GreetingId, new byte[] { 1 }));
        }
    
        [Test]
        public void ClientSessionAcceptsAConnectorFromOutsideThePackage()
        {
            var listener = new LoopbackListener(64);
            var server = new ServerSession(TestSchema(), new SessionConfig(), new SessionLimits(), new MessageDispatcher(TestSchema()), TestBundles.Protocol());
            server.Start(listener);
            var client = new ClientSession(TestSchema(), new SessionConfig(), new SessionLimits(), new MessageDispatcher(TestSchema()), TestBundles.Protocol());

            client.Start(new DelayedLoopbackConnector(listener, 3));
            TimeSpan now = TimeSpan.Zero;
            for (int step = 0; step < 20 && client.State != ConnectionState.Started; step++)
            {
                now += TimeSpan.FromMilliseconds(16);
                server.Tick(now);
                client.Tick(now);
            }

            Assert.AreEqual(ConnectionState.Started, client.State);
            Assert.AreEqual(1, server.PeerCount);
        }
    }
}
