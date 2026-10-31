using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using BundleFixture;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class GatewayModeTest
    {
        private static readonly byte[] Key = Enumerable.Range(1, ConnectTokens.KeyLength).Select(value => (byte)value).ToArray();
        private static readonly byte[] Hello = Encoding.ASCII.GetBytes("a frame through the gateway");

        private readonly List<IDisposable> owned = new List<IDisposable>();
        private ConnectTokens tokens;
        private EncryptedUdpTransportFactory clientFactory;
        private IListenerTransport listener;
        private ushort port;

        [SetUp]
        public void SetUp()
        {
            DateTime now = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
            tokens = new ConnectTokens(TestObjects.Registry(), Key, null, () => now);
            clientFactory = new EncryptedUdpTransportFactory(TestObjects.Registry());
            var serverFactory = new EncryptedUdpTransportFactory(TestObjects.Registry(), tokens, "pod-a", true);
            listener = Own(serverFactory.CreateListener(0, out port));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (IDisposable disposable in owned)
            {
                disposable.Dispose();
            }
        }

        [Test]
        public void TheEnvelopeHasTheBytesTheGatewayWrites()
        {
            var buffer = new byte[GatewayEnvelope.Length];
            GatewayEnvelope.Write(buffer, new IPEndPoint(IPAddress.Parse("203.0.113.7"), 51000));

            CollectionAssert.AreEqual(new byte[] { 1, 4, 0xC7, 0x38, 203, 0, 113, 7, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, buffer);
            Assert.IsTrue(GatewayEnvelope.TryRead(buffer, buffer.Length, out IPEndPoint v4));
            Assert.AreEqual(new IPEndPoint(IPAddress.Parse("203.0.113.7"), 51000), v4);

            var v6 = new IPEndPoint(IPAddress.Parse("2001:db8::7"), 443);
            GatewayEnvelope.Write(buffer, v6);
            Assert.AreEqual(6, buffer[1]);
            Assert.IsTrue(GatewayEnvelope.TryRead(buffer, buffer.Length, out IPEndPoint read));
            Assert.AreEqual(v6, read);
        }

        [Test]
        public void ABrokenEnvelopeHasNoClient()
        {
            var good = new byte[GatewayEnvelope.Length];
            GatewayEnvelope.Write(good, new IPEndPoint(IPAddress.Parse("203.0.113.7"), 51000));
            byte[] version = (byte[])good.Clone();
            version[0] = 2;
            byte[] family = (byte[])good.Clone();
            family[1] = 5;
            byte[] padding = (byte[])good.Clone();
            padding[19] = 1;

            Assert.IsFalse(GatewayEnvelope.TryRead(good, 19, out _));
            Assert.IsFalse(GatewayEnvelope.TryRead(version, version.Length, out _));
            Assert.IsFalse(GatewayEnvelope.TryRead(family, family.Length, out _));
            Assert.IsFalse(GatewayEnvelope.TryRead(padding, padding.Length, out _));
        }

        [Test]
        public void AClientThroughTheGatewayReachesThePodAndTheRepliesCarryItsAddress()
        {
            using (var gateway = new GatewayStub(port))
            {
                ITransport client = Connect(tokens.Issue("pod-a", 77, new byte[] { 5 }), gateway.Port);
                client.Send(Hello);
                gateway.Pump();
                ITransport peer = AcceptOne();
                CollectionAssert.AreEqual(Hello, ReceiveOne(peer));

                peer.Send(new byte[] { 1, 2, 3 });
                gateway.Pump();
                CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, ReceiveOne(client));
                client.Send(new byte[] { 4 });
                gateway.Pump();

                CollectionAssert.AreEqual(new byte[] { 4 }, ReceiveOne(peer));
                Assert.IsTrue(GatewayEnvelope.TryRead(gateway.FromPod(0), gateway.FromPod(0).Length, out IPEndPoint addressed));
                Assert.AreEqual(gateway.Client, addressed);
                Assert.AreEqual(77UL, BitConverter.ToUInt64(gateway.FromPod(0), GatewayEnvelope.Length));
            }
        }

        [Test]
        public void AListenerBehindAGatewayOpensNoPeerForADatagramWithoutAnEnvelope()
        {
            ITransport client = Connect(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty), port);
            client.Send(Hello);

            Assert.IsNull(AcceptOne(TimeSpan.FromMilliseconds(200)));
        }

        [Test]
        public void AForgedEnvelopeDoesNotMoveTheClientButAnOpenedOneDoes()
        {
            using (var gateway = new GatewayStub(port))
            {
                ITransport client = Connect(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty), gateway.Port);
                client.Send(Hello);
                gateway.Pump();
                ITransport peer = AcceptOne();
                ReceiveOne(peer);
                var elsewhere = new IPEndPoint(IPAddress.Loopback, 9);

                gateway.SendForged(BitConverter.GetBytes(77UL).Concat(new byte[] { 1 }).Concat(new byte[40]).ToArray(), elsewhere);
                Pump(gateway);
                peer.Send(new byte[] { 6 });
                gateway.Pump();
                IPEndPoint afterForgery = gateway.LastAddressed;

                gateway.Rewrite = elsewhere;
                client.Send(new byte[] { 7 });
                gateway.Pump();
                CollectionAssert.AreEqual(new byte[] { 7 }, ReceiveOne(peer));
                peer.Send(new byte[] { 8 });
                gateway.Pump();

                Assert.AreEqual(gateway.Client, afterForgery);
                Assert.AreEqual(elsewhere, gateway.LastAddressed);
            }
        }

        [Test]
        public void RepliesGoThroughTheGatewayThatCarriedTheLastOpenedDatagram()
        {
            using (var first = new GatewayStub(port))
            using (var second = new GatewayStub(port))
            {
                ITransport client = Connect(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty), first.Port);
                client.Send(Hello);
                first.Pump();
                ITransport peer = AcceptOne();
                ReceiveOne(peer);

                second.Carry(first.FromClient(0), first.Client);
                Pump(second);
                peer.Send(new byte[] { 9 });
                first.Pump();
                second.Pump();
                int throughFirst = first.FromPodCount;
                int throughSecond = second.FromPodCount;

                first.Hold = true;
                client.Send(new byte[] { 10 });
                first.Pump();
                second.Carry(first.FromClient(1), first.Client);
                second.Pump();
                CollectionAssert.AreEqual(new byte[] { 10 }, ReceiveOne(peer));
                peer.Send(new byte[] { 11 });
                first.Pump();
                second.Pump();

                Assert.AreEqual(1, throughFirst);
                Assert.AreEqual(0, throughSecond);
                Assert.AreEqual(1, first.FromPodCount);
                Assert.AreEqual(1, second.FromPodCount);
            }
        }

        [Test]
        public void AFrameThatLeavesNoRoomForTheEnvelopeIsRefused()
        {
            using (var gateway = new GatewayStub(port))
            {
                ITransport client = Connect(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty), gateway.Port);
                client.Send(Hello);
                gateway.Pump();
                ITransport peer = AcceptOne();
                int largest = EncryptedDatagram.MaxDatagram - EncryptedDatagram.DataOverhead - GatewayEnvelope.Length;

                Assert.AreEqual(TransportSignal.TooLarge, peer.Send(new byte[largest + 1]).Signal);
                Assert.AreEqual(TransportSignal.Ok, peer.Send(new byte[largest]).Signal);
            }
        }

        private void Pump(GatewayStub gateway)
        {
            gateway.Pump();
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 50)
            {
                listener.Accept();
                Thread.Sleep(1);
            }
        }

        private ITransport Connect(byte[] grant, ushort target)
        {
            ITransportConnector connector = Own(clientFactory.CreateConnector("127.0.0.1", target, grant));
            Stopwatch watch = Stopwatch.StartNew();
            ConnectStatus status;
            ITransport transport;
            while ((status = connector.Poll(out transport)) == ConnectStatus.Pending && watch.Elapsed < TimeSpan.FromSeconds(2))
            {
                Thread.Sleep(1);
            }

            Assert.AreEqual(ConnectStatus.Connected, status);
            return Own(transport);
        }

        private ITransport AcceptOne() => AcceptOne(TimeSpan.FromSeconds(2));

        private ITransport AcceptOne(TimeSpan timeout)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.Elapsed < timeout)
            {
                AcceptOutcome outcome = listener.Accept();
                if (outcome.Status == AcceptStatus.Accepted)
                {
                    return Own(outcome.Transport);
                }

                if (outcome.Status == AcceptStatus.Pending)
                {
                    Thread.Sleep(1);
                }
            }

            return null;
        }

        private byte[] ReceiveOne(ITransport transport)
        {
            var buffer = new byte[65536];
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(2))
            {
                listener.Accept();
                ReceiveOutcome outcome = transport.Receive(buffer);
                if (outcome.Signal == TransportSignal.Ok)
                {
                    return buffer.Take(outcome.Count).ToArray();
                }

                Thread.Sleep(1);
            }

            return null;
        }

        private T Own<T>(T disposable)
            where T : IDisposable
        {
            if (disposable != null)
            {
                owned.Add(disposable);
            }

            return disposable;
        }

        private sealed class GatewayStub : IDisposable
        {
            private readonly IPEndPoint pod;
            private readonly UdpClient front = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            private readonly UdpClient back = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            private readonly List<byte[]> fromClient = new List<byte[]>();
            private readonly List<byte[]> fromPod = new List<byte[]>();

            public GatewayStub(ushort podPort)
            {
                pod = new IPEndPoint(IPAddress.Loopback, podPort);
            }

            public ushort Port => (ushort)((IPEndPoint)front.Client.LocalEndPoint).Port;

            public IPEndPoint Client { get; private set; }

            public IPEndPoint Rewrite { get; set; }

            public bool Hold { get; set; }

            public IPEndPoint LastAddressed { get; private set; }

            public int FromPodCount => fromPod.Count;

            public byte[] FromPod(int index) => fromPod[index];

            public byte[] FromClient(int index) => fromClient[index];

            public void Carry(byte[] datagram, IPEndPoint client)
            {
                Client = client;
                Send(datagram, client);
            }

            public void SendForged(byte[] datagram, IPEndPoint client) => Send(datagram, client);

            public void Pump()
            {
                Thread.Sleep(20);
                while (front.Available > 0)
                {
                    IPEndPoint from = null;
                    byte[] bytes = front.Receive(ref from);
                    Client = from;
                    fromClient.Add(bytes);
                    if (!Hold)
                    {
                        Send(bytes, Rewrite ?? from);
                    }
                }

                while (back.Available > 0)
                {
                    IPEndPoint from = null;
                    byte[] bytes = back.Receive(ref from);
                    fromPod.Add(bytes);
                    Assert.IsTrue(GatewayEnvelope.TryRead(bytes, bytes.Length, out IPEndPoint addressed));
                    LastAddressed = addressed;
                    if (addressed.Equals(Client))
                    {
                        front.Send(bytes.Skip(GatewayEnvelope.Length).ToArray(), bytes.Length - GatewayEnvelope.Length, addressed);
                    }
                }

                Thread.Sleep(20);
            }

            public void Dispose()
            {
                front.Dispose();
                back.Dispose();
            }

            private void Send(byte[] datagram, IPEndPoint client)
            {
                var wrapped = new byte[GatewayEnvelope.Length + datagram.Length];
                GatewayEnvelope.Write(wrapped, client);
                Buffer.BlockCopy(datagram, 0, wrapped, GatewayEnvelope.Length, datagram.Length);
                back.Send(wrapped, wrapped.Length, pod);
            }
        }
    }
}
