using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class MultiTransportTest
    {
        private const uint GreetingId = 0x2000_0001;
        private const int SmallBudget = FomoxaWire.DataFrameHeaderSize + BundleFormat.BundleOverhead + BundleFormat.EntryOverhead + 1;

        private static Schema HostSchema() =>
            new Schema(0xCAFE, new[] { new MessageSchema(GreetingId, 0xF00D, new ulong[] { 0xF00D }) });

        [Test]
        public void ACompositeListenerQueuesTheBudgetOfEachAcceptedConnectionInOrder()
        {
            var first = new LoopbackListener(8);
            var second = new LoopbackListener(8);
            var inner = new CompositeListener(new IListenerTransport[] { second }, new[] { 300 });
            var outer = new CompositeListener(new IListenerTransport[] { first, inner }, new[] { 100, 200 });
            second.Connect();
            first.Connect();
            first.Connect();

            var budgets = new List<int>();
            for (int poll = 0; poll < 6; poll++)
            {
                if (outer.Accept().Status == AcceptStatus.Accepted && outer.TryTakeAcceptedBudget(out int budget))
                {
                    budgets.Add(budget);
                }
            }

            CollectionAssert.AreEqual(new[] { 100, 300, 100 }, budgets);
            Assert.IsFalse(outer.TryTakeAcceptedBudget(out _));
            Assert.IsFalse(new CompositeListener(new LoopbackListener(1)).TryTakeAcceptedBudget(out _));
            outer.Dispose();
        }

        [Test]
        public void EachPeerBundlesWithTheBudgetOfTheListenerItCameThrough()
        {
            var small = new LoopbackListener(64);
            var large = new LoopbackListener(64);
            var server = new ServerSession(HostSchema(), new SessionConfig(), new SessionLimits(), new MessageDispatcher(HostSchema()), TestBundles.Protocol());
            server.Start(new CompositeListener(new IListenerTransport[] { small, large }, new[] { SmallBudget, FomoxaWire.MaxDataFrameSize }));
            var peers = new List<ulong>();
            server.OnRemoteConnectionState += args =>
            {
                if (args.State == ConnectionState.Starting)
                {
                    peers.Add(args.PeerId);
                }
            };
            TimeSpan now = TimeSpan.Zero;
            using var throughLarge = FomoxaConnection.Connect(large.Connect(), HostSchema(), new SessionConfig(), now);
            now = Handshake(server, throughLarge, now);
            using var throughSmall = FomoxaConnection.Connect(small.Connect(), HostSchema(), new SessionConfig(), now);
            now = Handshake(server, throughSmall, now);
            throughLarge.Tick(now);

            Assert.AreEqual(2, peers.Count);
            ulong largePeer = peers[0];
            ulong smallPeer = peers[1];
            Assert.AreEqual(FomoxaWire.MaxDataFrameSize, server.FrameBudgetOf(largePeer));
            Assert.AreEqual(SmallBudget, server.FrameBudgetOf(smallPeer));
            foreach (ulong peerId in peers)
            {
                for (byte value = 1; value <= 3; value++)
                {
                    server.Send(peerId, GreetingId, new[] { value });
                }
            }

            server.Flush();
            now += TimeSpan.FromMilliseconds(16);

            Assert.AreEqual(3, BundlesReceived(throughSmall, now));
            Assert.AreEqual(1, BundlesReceived(throughLarge, now));
            server.Stop();
        }

        [Test]
        public void TheMultiFactoryListensOnEveryTransportAndConnectsThroughTheFirst()
        {
            ushort webSocketPort = FreePort();
            var factory = new MultiTransportFactory(
                new ITransportFactory[] { new TcpTransportFactory(), new WebSocketTransportFactory() },
                new ushort[] { 0, webSocketPort });
            using IListenerTransport listener = factory.CreateListener(0, out ushort tcpPort);
            Assert.AreNotEqual(0, tcpPort);
            Assert.AreEqual(FomoxaWire.MaxDataFrameSize, factory.FrameBudget);

            using ITransportConnector throughFirst = factory.CreateConnector("127.0.0.1", tcpPort);
            using ITransportConnector webSocket = new WebSocketTransportFactory().CreateConnector("127.0.0.1", webSocketPort);
            var accepted = new List<ITransport>();
            ITransport tcpClient = null;
            ITransport webSocketClient = null;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while ((accepted.Count < 2 || tcpClient == null || webSocketClient == null) && clock.Elapsed < TimeSpan.FromSeconds(10))
            {
                AcceptOutcome outcome = listener.Accept();
                if (outcome.Status == AcceptStatus.Accepted)
                {
                    accepted.Add(outcome.Transport);
                }

                if (tcpClient == null)
                {
                    throughFirst.Poll(out tcpClient);
                }

                if (webSocketClient == null)
                {
                    webSocket.Poll(out webSocketClient);
                }

                System.Threading.Thread.Sleep(1);
            }

            Assert.AreEqual(2, accepted.Count);
            Assert.IsInstanceOf<TcpTransport>(tcpClient);
            Assert.AreEqual(TransportKind.Message, webSocketClient.Kind);
            foreach (ITransport transport in new[] { tcpClient, webSocketClient, accepted[0], accepted[1] })
            {
                transport.Dispose();
            }
        }

        [Test]
        public void AListenerThatCannotStartReleasesTheOnesAlreadyStarted()
        {
            ushort firstPort = FreePort();
            using var blocker = new TcpListenerTransport(new IPEndPoint(IPAddress.Any, 0));
            var factory = new MultiTransportFactory(
                new ITransportFactory[] { new TcpTransportFactory(), new TcpTransportFactory() },
                new ushort[] { 0, (ushort)blocker.LocalEndPoint.Port });

            Assert.Throws<SocketException>(() => factory.CreateListener(firstPort, out _));
            using IListenerTransport reopened = new TcpTransportFactory().CreateListener(firstPort, out ushort bound);
            Assert.AreEqual(firstPort, bound);
            Assert.Throws<ArgumentException>(() => new MultiTransportFactory(new ITransportFactory[0], new ushort[0]));
            Assert.Throws<ArgumentException>(() => new MultiTransportFactory(new ITransportFactory[] { new TcpTransportFactory() }, new ushort[0]));
        }

        private static TimeSpan Handshake(ServerSession server, FomoxaConnection client, TimeSpan now)
        {
            for (int step = 0; step < 30 && !client.IsReady; step++)
            {
                now += TimeSpan.FromMilliseconds(16);
                server.Tick(now);
                client.Tick(now);
            }

            Assert.IsTrue(client.IsReady);
            return now;
        }

        private static int BundlesReceived(FomoxaConnection client, TimeSpan now)
        {
            int bundles = 0;
            foreach (FomoxaEvent raised in client.Tick(now))
            {
                if (raised.Kind == FomoxaEventKind.Message && raised.MessageId == TestBundles.MessageId)
                {
                    bundles++;
                }
            }

            return bundles;
        }

        private static ushort FreePort()
        {
            using var probe = new TcpListenerTransport(new IPEndPoint(IPAddress.Any, 0));
            return (ushort)probe.LocalEndPoint.Port;
        }
    }
}
