using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class TransportFactoryTest
    {
        private sealed class ScriptedResolver : IHostResolver
        {
            private readonly int pendingPolls;
            private readonly IPAddress[] addresses;
            private int polls;

            public ScriptedResolver(int pendingPolls, params IPAddress[] addresses)
            {
                this.pendingPolls = pendingPolls;
                this.addresses = addresses;
            }

            public ResolveStatus Poll(out IPAddress[] resolved)
            {
                resolved = null;
                if (polls++ < pendingPolls)
                {
                    return ResolveStatus.Pending;
                }

                if (addresses.Length == 0)
                {
                    return ResolveStatus.Failed;
                }

                resolved = addresses;
                return ResolveStatus.Resolved;
            }
        }

        private static ConnectStatus PollUntilDone(ITransportConnector connector, IListenerTransport listener, out ITransport transport)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(10))
            {
                listener?.Accept();
                ConnectStatus status = connector.Poll(out transport);
                if (status != ConnectStatus.Pending)
                {
                    return status;
                }

                Thread.Sleep(1);
            }

            transport = null;
            return ConnectStatus.Pending;
        }

        private static ushort ClosedPort()
        {
            using var probe = new TcpListenerTransport(new IPEndPoint(IPAddress.Loopback, 0));
            return (ushort)probe.LocalEndPoint.Port;
        }

        [Test]
        public void TcpConnectsToAListenerByAddress()
        {
            using var listener = new TcpListenerTransport(new IPEndPoint(IPAddress.Loopback, 0));
            using ITransportConnector connector = new TcpTransportFactory().CreateConnector("127.0.0.1", (ushort)listener.LocalEndPoint.Port);

            Assert.AreEqual(ConnectStatus.Connected, PollUntilDone(connector, listener, out ITransport transport));
            Assert.IsInstanceOf<TcpTransport>(transport);
            transport.Dispose();
        }

        [Test]
        public void TcpConnectsToAListenerByHostName()
        {
            using var listener = new TcpListenerTransport(new IPEndPoint(IPAddress.Loopback, 0));
            using ITransportConnector connector = new TcpTransportFactory().CreateConnector("localhost", (ushort)listener.LocalEndPoint.Port);

            Assert.AreEqual(ConnectStatus.Connected, PollUntilDone(connector, listener, out ITransport transport));
            transport.Dispose();
        }

        [Test]
        public void TcpRefusedConnectionFails()
        {
            using ITransportConnector connector = new TcpTransportFactory().CreateConnector("127.0.0.1", ClosedPort());

            Assert.AreEqual(ConnectStatus.Failed, PollUntilDone(connector, null, out ITransport transport));
            Assert.IsNull(transport);
        }

        [Test]
        public void TcpPollAfterDisposeFails()
        {
            using var listener = new TcpListenerTransport(new IPEndPoint(IPAddress.Loopback, 0));
            ITransportConnector connector = new TcpTransportFactory().CreateConnector("127.0.0.1", (ushort)listener.LocalEndPoint.Port);
            connector.Poll(out _);

            connector.Dispose();

            Assert.AreEqual(ConnectStatus.Failed, connector.Poll(out ITransport transport));
            Assert.IsNull(transport);
        }

        [Test]
        public void TcpFactoryListenerReportsItsPortAndAcceptsATcpConnector()
        {
            var factory = new TcpTransportFactory();
            using IListenerTransport listener = factory.CreateListener(0, out ushort port);
            Assert.AreNotEqual(0, port);
            using ITransportConnector connector = factory.CreateConnector("127.0.0.1", port);

            Assert.AreEqual(ConnectStatus.Connected, PollUntilDone(connector, listener, out ITransport transport));
            transport.Dispose();
        }

        [Test]
        public void UdpConnectsWithoutWaitingForThePeer()
        {
            using ITransportConnector connector = new UdpTransportFactory().CreateConnector("127.0.0.1", 9);

            Assert.AreEqual(ConnectStatus.Connected, connector.Poll(out ITransport transport));
            Assert.IsInstanceOf<UdpTransport>(transport);
            transport.Dispose();
        }
    
        [Test]
        public void UdpFactoryListenerReportsItsPort()
        {
            using IListenerTransport listener = new UdpTransportFactory().CreateListener(0, out ushort port);

            Assert.AreNotEqual(0, port);
        }

        [Test]
        public void TcpConnectorIsPendingWhileTheResolverIsPending()
        {
            using var listener = new TcpListenerTransport(new IPEndPoint(IPAddress.Loopback, 0));
            using var connector = new TcpConnector(new ScriptedResolver(3, IPAddress.Loopback), (ushort)listener.LocalEndPoint.Port);

            for (int poll = 0; poll < 3; poll++)
            {
                Assert.AreEqual(ConnectStatus.Pending, connector.Poll(out _));
            }

            Assert.AreEqual(ConnectStatus.Connected, PollUntilDone(connector, listener, out ITransport transport));
            transport.Dispose();
        }

        [Test]
        public void ConnectorsFailWhenResolutionFails()
        {
            using var tcp = new TcpConnector(new ScriptedResolver(0), 9);
            using var udp = new UdpConnector(new ScriptedResolver(0), 9);

            Assert.AreEqual(ConnectStatus.Failed, tcp.Poll(out _));
            Assert.AreEqual(ConnectStatus.Failed, udp.Poll(out _));
        }

        [Test]
        public void TcpConnectorTriesTheNextAddressWhenOneIsRefused()
        {
            using var listener = new TcpListenerTransport(new IPEndPoint(IPAddress.Loopback, 0));
            var resolver = new ScriptedResolver(0, IPAddress.IPv6Loopback, IPAddress.Loopback);
            using var connector = new TcpConnector(resolver, (ushort)listener.LocalEndPoint.Port);

            Assert.AreEqual(ConnectStatus.Connected, PollUntilDone(connector, listener, out ITransport transport));
            transport.Dispose();
        }
    }
}
