using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Transports;
using NUnit.Framework;
using Org.BouncyCastle.Tls;

namespace Fomoxa.Networking.Tests
{
    public sealed class TlsTransportTest
    {
        private const uint GreetingId = 0x2000_0001;

        private static readonly TestCertificate Root = TestCertificates.Root("Test Root");
        private static readonly TestCertificate Middle = TestCertificates.Intermediate(Root, "Test Intermediate");
        private static readonly TestCertificate Leaf = TestCertificates.Leaf(Middle);

        private static Schema TestSchema() =>
            new Schema(0xCAFE, new[] { new MessageSchema(GreetingId, 0xF00D, new ulong[] { 0xF00D }) });

        private static TlsSettings Trusting(TestCertificate root) =>
            new TlsSettings { CertificateCheck = new TlsCertificateCheck(TlsRoots.FromPem(root.Pem)) };

        [Test]
        public void TcpOverTlsCarriesMessagesLargerThanATlsRecordBothWays()
        {
            using var rig = new Rig(new TcpTransportFactory());
            using var proxy = new TlsTestProxy(rig.BackendPort, Leaf, Middle);
            var factory = new TcpTransportFactory(Trusting(Root));
            var large = new byte[40 * 1024];
            new Random(1116).NextBytes(large);

            rig.Connect(() => factory.CreateConnector("localhost", proxy.Port));
            rig.Client.Send(GreetingId, large);
            rig.RunUntil(() => rig.ServerReceived.Count == 1);
            rig.Server.Broadcast(GreetingId, large);
            rig.RunUntil(() => rig.ClientReceived.Count == 1);

            Assert.AreEqual(ConnectionState.Started, rig.Client.State);
            Assert.AreEqual(large, rig.ServerReceived[0]);
            Assert.AreEqual(large, rig.ClientReceived[0]);
            CollectionAssert.AreEqual(new[] { "localhost" }, proxy.ServerNames);
        }

        [Test]
        public void TheClientOffersTls13AndFallsBackToTls12()
        {
            foreach (ProtocolVersion version in new[] { ProtocolVersion.TLSv12, ProtocolVersion.TLSv13 })
            {
                using var rig = new Rig(new TcpTransportFactory());
                using var proxy = new TlsTestProxy(rig.BackendPort, Leaf, Middle) { Versions = new[] { version } };
                var factory = new TcpTransportFactory(Trusting(Root));

                rig.Connect(() => factory.CreateConnector("127.0.0.1", proxy.Port));
                rig.Client.Send(GreetingId, new byte[] { 7 });
                rig.RunUntil(() => rig.ServerReceived.Count == 1);

                Assert.AreEqual(1, rig.ServerReceived.Count, version.ToString());
                CollectionAssert.AreEqual(new[] { version }, proxy.NegotiatedVersions);
                CollectionAssert.AreEqual(new string[] { null }, proxy.ServerNames);
            }
        }

        [Test]
        public void TheClientRefusesTls11()
        {
            using var rig = new Rig(new TcpTransportFactory());
            using var proxy = new TlsTestProxy(rig.BackendPort, Leaf, Middle) { Versions = new[] { ProtocolVersion.TLSv11 } };
            var factory = new TcpTransportFactory(Trusting(Root));

            rig.Connect(() => factory.CreateConnector("localhost", proxy.Port));
            rig.RunUntil(() => rig.Stops.Count == 1);

            Assert.AreEqual(StopReason.TlsRejected, rig.Stops[0].Reason);
            Assert.AreEqual(0, proxy.NegotiatedVersions.Count);
        }

        [Test]
        public void AnUntrustedCertificateStopsWithoutRetryAndNamesTheReason()
        {
            using var rig = new Rig(new TcpTransportFactory());
            using var proxy = new TlsTestProxy(rig.BackendPort, Leaf, Middle);
            var factory = new TcpTransportFactory(Trusting(TestCertificates.Root("Other Root")));

            rig.Connect(() => factory.CreateConnector("localhost", proxy.Port));
            rig.RunUntil(() => rig.Stops.Count == 1);
            rig.Run(TimeSpan.FromMilliseconds(200));

            Assert.AreEqual(1, rig.Stops.Count);
            Assert.AreEqual(StopReason.TlsRejected, rig.Stops[0].Reason);
            Assert.IsFalse(rig.Stops[0].WillRetry);
            Assert.AreEqual(1, rig.Rejections.Count);
            StringAssert.StartsWith("the TLS server localhost was rejected: the server certificate does not lead to a trusted root", rig.Rejections[0]);
            Assert.AreEqual(0, rig.ServerReceived.Count);
        }

        [Test]
        public void ACertificateForAnotherHostStopsWithoutRetry()
        {
            using var rig = new Rig(new TcpTransportFactory());
            TestCertificate namedOnly = TestCertificates.Leaf(Middle, new LeafSpec { Addresses = new string[0] });
            using var proxy = new TlsTestProxy(rig.BackendPort, namedOnly, Middle);
            var factory = new TcpTransportFactory(Trusting(Root));

            rig.Connect(() => factory.CreateConnector("127.0.0.1", proxy.Port));
            rig.RunUntil(() => rig.Stops.Count == 1);

            Assert.AreEqual(StopReason.TlsRejected, rig.Stops[0].Reason);
            Assert.IsFalse(rig.Stops[0].WillRetry);
            Assert.AreEqual("the TLS server 127.0.0.1 was rejected: the server certificate is not for 127.0.0.1", rig.Rejections[0]);
        }

        [Test]
        public void AnAlertFromTheServerStopsWithoutRetry()
        {
            using var rig = new Rig(new TcpTransportFactory());
            using var proxy = new TlsTestProxy(rig.BackendPort, Leaf, Middle) { RefuseHandshake = true };
            var factory = new TcpTransportFactory(Trusting(Root));

            rig.Connect(() => factory.CreateConnector("localhost", proxy.Port));
            rig.RunUntil(() => rig.Stops.Count == 1);

            Assert.AreEqual(StopReason.TlsRejected, rig.Stops[0].Reason);
            Assert.IsFalse(rig.Stops[0].WillRetry);
            Assert.AreEqual("the TLS server localhost ended the handshake with alert handshake_failure(40)", rig.Rejections[0]);
        }

        [Test]
        public void TheApplicationCheckReplacesTheDefaultCheck()
        {
            using var rig = new Rig(new TcpTransportFactory());
            TestCertificate selfSigned = TestCertificates.Root("localhost");
            using var proxy = new TlsTestProxy(rig.BackendPort, TestCertificates.Leaf(selfSigned));
            var pinned = new PinnedCheck(selfSigned);
            var factory = new TcpTransportFactory(new TlsSettings { CertificateCheck = pinned });

            rig.Connect(() => factory.CreateConnector("localhost", proxy.Port));
            rig.Client.Send(GreetingId, new byte[] { 3 });
            rig.RunUntil(() => rig.ServerReceived.Count == 1);

            Assert.AreEqual(1, rig.ServerReceived.Count);
            Assert.AreEqual("localhost", pinned.Hosts[0]);
            Assert.AreEqual(1, pinned.ChainLengths[0]);
        }

        [Test]
        public void AnApplicationCheckThatRejectsOrThrowsStopsWithItsReason()
        {
            var cases = new (ITlsCertificateCheck Check, string Rejection)[]
            {
                (new FixedCheck(false, "pin mismatch", null), "the TLS server localhost was rejected: pin mismatch"),
                (new FixedCheck(false, null, null), "the TLS server localhost was rejected: the certificate check rejected the certificate"),
                (new FixedCheck(true, null, new InvalidOperationException("broken check")), "the TLS server localhost was rejected: the certificate check threw InvalidOperationException: broken check"),
            };
            foreach ((ITlsCertificateCheck check, string expected) in cases)
            {
                using var rig = new Rig(new TcpTransportFactory());
                using var proxy = new TlsTestProxy(rig.BackendPort, Leaf, Middle);
                var factory = new TcpTransportFactory(new TlsSettings { CertificateCheck = check });

                rig.Connect(() => factory.CreateConnector("localhost", proxy.Port));
                rig.RunUntil(() => rig.Stops.Count == 1);

                Assert.AreEqual(StopReason.TlsRejected, rig.Stops[0].Reason);
                Assert.AreEqual(expected, rig.Rejections[0]);
            }
        }

        [Test]
        public void AServerThatClosesDuringTheHandshakeIsATransportErrorAndIsRetried()
        {
            using var rig = new Rig(new TcpTransportFactory());
            using var server = new RawTestServer(null, true);
            var factory = new TcpTransportFactory(Trusting(Root));

            rig.Connect(() => factory.CreateConnector("localhost", server.Port));
            rig.RunUntil(() => rig.Stops.Count == 2);

            Assert.AreEqual(StopReason.TransportError, rig.Stops[0].Reason);
            Assert.IsTrue(rig.Stops[0].WillRetry);
            Assert.AreEqual(0, rig.Rejections.Count);
            Assert.GreaterOrEqual(server.Accepted, 2);
        }

        [Test]
        public void BytesThatAreNotTlsAreATransportErrorAndAreRetried()
        {
            using var rig = new Rig(new TcpTransportFactory());
            using var server = new RawTestServer(Encoding.ASCII.GetBytes("HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\n\r\n"), false);
            var factory = new TcpTransportFactory(Trusting(Root));

            rig.Connect(() => factory.CreateConnector("localhost", server.Port));
            rig.RunUntil(() => rig.Stops.Count == 1);

            Assert.AreEqual(StopReason.TransportError, rig.Stops[0].Reason);
            Assert.IsTrue(rig.Stops[0].WillRetry);
            Assert.AreEqual(0, rig.Rejections.Count);
        }

        [Test]
        public void ATlsHandshakeThatDoesNotFinishInTimeIsATransportError()
        {
            using var server = new RawTestServer(null, false);
            TimeSpan now = TimeSpan.Zero;
            TlsSettings tls = Trusting(Root);
            tls.HandshakeTimeout = TimeSpan.FromSeconds(2);
            tls.Clock = () => now;
            using ITransportConnector connector = new TcpTransportFactory(tls).CreateConnector("127.0.0.1", server.Port);

            ConnectStatus status = PollFor(connector, TimeSpan.FromMilliseconds(300));
            Assert.AreEqual(ConnectStatus.Pending, status);
            now = TimeSpan.FromSeconds(2);
            Assert.AreEqual(ConnectStatus.Pending, connector.Poll(out _));
            now = TimeSpan.FromSeconds(2.001);
            Assert.AreEqual(ConnectStatus.Failed, connector.Poll(out _));
            Assert.AreEqual(ConnectStatus.Failed, connector.Poll(out _));
        }

        [Test]
        public void AServerCloseAfterTheHandshakeEndsTheSessionAsPeerClosed()
        {
            using var rig = new Rig(new TcpTransportFactory());
            using var proxy = new TlsTestProxy(rig.BackendPort, Leaf, Middle);
            var factory = new TcpTransportFactory(Trusting(Root));

            rig.Connect(() => factory.CreateConnector("localhost", proxy.Port));
            rig.RunUntil(() => rig.Client.State == ConnectionState.Started);
            proxy.Dispose();
            rig.RunUntil(() => rig.Stops.Count == 1);

            Assert.AreEqual(1, rig.Stops.Count);
            Assert.AreNotEqual(StopReason.TlsRejected, rig.Stops[0].Reason);
            Assert.IsTrue(rig.Stops[0].WillRetry);
        }

        [Test]
        public void ACloseNotifyFromTheServerReadsAsTheEndOfTheStream()
        {
            foreach (bool keepSocket in new[] { true, false })
            {
                using var proxy = new TlsTestProxy(1, Leaf, Middle) { CloseAfterHandshake = true, KeepSocketAfterClose = keepSocket };
                using ITransportConnector connector = new TcpTransportFactory(Trusting(Root)).CreateConnector("localhost", proxy.Port);
                ITransport transport = ConnectedTransport(connector);
                var buffer = new byte[16];
                ReceiveOutcome outcome = default;
                var clock = Stopwatch.StartNew();
                while (clock.Elapsed < TimeSpan.FromSeconds(5))
                {
                    outcome = transport.Receive(buffer);
                    if (outcome.Signal != TransportSignal.WouldBlock)
                    {
                        break;
                    }

                    Thread.Sleep(1);
                }

                Assert.AreEqual(TransportKind.Stream, transport.Kind);
                Assert.AreEqual(TransportSignal.Closed, outcome.Signal, keepSocket ? "socket kept open" : "socket closed");
                Assert.AreEqual(TransportSignal.Closed, transport.Receive(buffer).Signal);
                transport.Dispose();
                Assert.AreEqual(SendOutcome.Closed, transport.Send(new byte[] { 1 }));
            }
        }

        [Test]
        public void SentBytesLeaveWithoutWaitingForAReceive()
        {
            using var backend = new BackendListener();
            using var proxy = new TlsTestProxy(backend.Port, Leaf, Middle);
            using ITransportConnector connector = new TcpTransportFactory(Trusting(Root)).CreateConnector("localhost", proxy.Port);
            ITransport transport = ConnectedTransport(connector);
            using Socket accepted = backend.Accept();
            var payload = new byte[3000];
            new Random(1118).NextBytes(payload);

            Assert.AreEqual(SendOutcome.Ok, transport.Send(payload));

            Assert.AreEqual(payload, ReadExactly(accepted, payload.Length));
            transport.Dispose();
        }

        [Test]
        public void ASendTheSocketCannotTakeIsWouldBlockAndLosesNoBytes()
        {
            using var backend = new BackendListener();
            using var proxy = new TlsTestProxy(backend.Port, Leaf, Middle);
            using ITransportConnector connector = new TcpTransportFactory(Trusting(Root)).CreateConnector("localhost", proxy.Port);
            ITransport transport = ConnectedTransport(connector);
            using Socket accepted = backend.Accept();

            long acceptedBytes = Backpressure.FillUntilWouldBlock(transport, new byte[64 * 1024]);

            Assert.AreEqual(acceptedBytes, Backpressure.Drain(accepted, transport, acceptedBytes));
            transport.Dispose();
        }

        [Test]
        public void CloseGracefullySendsACloseNotify()
        {
            using var backend = new BackendListener();
            using var proxy = new TlsTestProxy(backend.Port, Leaf, Middle);
            using ITransportConnector connector = new TcpTransportFactory(Trusting(Root)).CreateConnector("localhost", proxy.Port);
            ITransport transport = ConnectedTransport(connector);
            using Socket accepted = backend.Accept();

            transport.CloseGracefully();
            var clock = Stopwatch.StartNew();
            while (proxy.CloseNotifies == 0 && clock.Elapsed < TimeSpan.FromSeconds(5))
            {
                Thread.Sleep(1);
            }

            Assert.AreEqual(1, proxy.CloseNotifies);
            transport.Dispose();
        }

        [Test]
        public void TcpWithoutTlsSettingsStaysPlain()
        {
            Assert.IsNull(new TcpTransportFactory().Tls);
            using ITransportConnector plain = new TcpTransportFactory().CreateConnector("127.0.0.1", 1);
            using ITransportConnector secure = new TcpTransportFactory(new TlsSettings()).CreateConnector("127.0.0.1", 1);

            Assert.IsNotInstanceOf<ITlsRejection>(plain);
            Assert.IsInstanceOf<ITlsRejection>(secure);
        }

        [Test]
        public void TlsSettingsRefuseAMissingCheckAndANonPositiveTimeout()
        {
            var settings = new TlsSettings();

            Assert.IsInstanceOf<TlsCertificateCheck>(settings.CertificateCheck);
            Assert.AreSame(TlsRoots.Mozilla, ((TlsCertificateCheck)settings.CertificateCheck).Roots);
            Assert.AreEqual(TimeSpan.FromSeconds(5), settings.HandshakeTimeout);
            Assert.Throws<ArgumentNullException>(() => settings.CertificateCheck = null);
            Assert.Throws<ArgumentOutOfRangeException>(() => settings.HandshakeTimeout = TimeSpan.Zero);
        }

        [Test]
        public void WssCarriesMessagesThroughATlsProxy()
        {
            var settings = new WebSocketSettings { Path = "/play", Tls = Trusting(Root) };
            using var rig = new Rig(new WebSocketTransportFactory(settings));
            using var proxy = new TlsTestProxy(rig.BackendPort, Leaf, Middle);
            var factory = new WebSocketTransportFactory(settings);
            var large = new byte[40 * 1024];
            new Random(1117).NextBytes(large);

            rig.Connect(() => factory.CreateConnector($"wss://localhost:{proxy.Port}/play", 0));
            rig.Client.Send(GreetingId, large);
            rig.RunUntil(() => rig.ServerReceived.Count == 1);
            rig.Server.Broadcast(GreetingId, large);
            rig.RunUntil(() => rig.ClientReceived.Count == 1);

            Assert.AreEqual(large, rig.ServerReceived[0]);
            Assert.AreEqual(large, rig.ClientReceived[0]);
            CollectionAssert.AreEqual(new[] { "localhost" }, proxy.ServerNames);
        }

        [Test]
        public void WssWithAnUntrustedCertificateStopsWithoutRetry()
        {
            var settings = new WebSocketSettings { Tls = Trusting(TestCertificates.Root("Other Root")) };
            using var rig = new Rig(new WebSocketTransportFactory(settings));
            using var proxy = new TlsTestProxy(rig.BackendPort, Leaf, Middle);
            var factory = new WebSocketTransportFactory(settings);

            rig.Connect(() => factory.CreateConnector($"wss://localhost:{proxy.Port}/", 0));
            rig.RunUntil(() => rig.Stops.Count == 1);

            Assert.AreEqual(StopReason.TlsRejected, rig.Stops[0].Reason);
            Assert.IsFalse(rig.Stops[0].WillRetry);
            StringAssert.StartsWith("the TLS server localhost was rejected", rig.Rejections[0]);
        }

        [Test]
        public void TheWebSocketHandshakeTimeoutCoversTheTlsHandshake()
        {
            using var server = new RawTestServer(null, false);
            TimeSpan now = TimeSpan.Zero;
            var settings = new WebSocketSettings { HandshakeTimeout = TimeSpan.FromSeconds(3), Clock = () => now, Tls = Trusting(Root) };
            settings.Tls.HandshakeTimeout = TimeSpan.FromSeconds(60);
            using ITransportConnector connector = new WebSocketTransportFactory(settings).CreateConnector($"wss://127.0.0.1:{server.Port}/", 0);

            Assert.AreEqual(ConnectStatus.Pending, PollFor(connector, TimeSpan.FromMilliseconds(300)));
            now = TimeSpan.FromSeconds(3);
            Assert.AreEqual(ConnectStatus.Pending, connector.Poll(out _));
            now = TimeSpan.FromSeconds(3.001);
            Assert.AreEqual(ConnectStatus.Failed, connector.Poll(out _));
        }

        [Test]
        public void AWssAddressUsesTlsAndPort443ByDefault()
        {
            WebSocketTransportFactory.Endpoint secure = WebSocketTransportFactory.Parse("wss://game.example.com/play?room=1", 7000, "/");
            WebSocketTransportFactory.Endpoint plain = WebSocketTransportFactory.Parse("ws://game.example.com/play", 7000, "/");
            WebSocketTransportFactory.Endpoint bare = WebSocketTransportFactory.Parse("game.example.com", 7000, "/play");

            Assert.IsTrue(secure.Secure);
            Assert.AreEqual(443, secure.Port);
            Assert.AreEqual("game.example.com", secure.Host);
            Assert.AreEqual("/play?room=1", secure.Target);
            Assert.IsFalse(plain.Secure);
            Assert.AreEqual(80, plain.Port);
            Assert.IsFalse(bare.Secure);
            Assert.AreEqual(7000, WebSocketTransportFactory.Parse("wss://[::1]:7000/", 0, "/").Port);
            Assert.AreEqual("::1", WebSocketTransportFactory.Parse("wss://[::1]:7000/", 0, "/").Host);
            Assert.Throws<ArgumentException>(() => WebSocketTransportFactory.Parse("wss://", 0, "/"));
            Assert.Throws<ArgumentNullException>(() => new WebSocketSettings().Tls = null);
            Assert.IsInstanceOf<ITlsRejection>(new WebSocketTransportFactory().CreateConnector("wss://127.0.0.1:1/", 0));
        }

        private static byte[] ReadExactly(Socket socket, int length)
        {
            var read = new byte[length];
            int used = 0;
            var clock = Stopwatch.StartNew();
            while (used < length && clock.Elapsed < TimeSpan.FromSeconds(5))
            {
                if (!socket.Poll(1000, SelectMode.SelectRead))
                {
                    continue;
                }

                int count = socket.Receive(read, used, length - used, SocketFlags.None);
                if (count == 0)
                {
                    break;
                }

                used += count;
            }

            return read;
        }

        private sealed class BackendListener : IDisposable
        {
            private readonly Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            public BackendListener()
            {
                listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                listener.Listen(4);
            }

            public int Port => ((IPEndPoint)listener.LocalEndPoint).Port;

            public Socket Accept()
            {
                Assert.IsTrue(listener.Poll(5_000_000, SelectMode.SelectRead), "the proxy did not connect to the backend");
                return listener.Accept();
            }

            public void Dispose() => listener.Dispose();
        }

        private static ConnectStatus PollFor(ITransportConnector connector, TimeSpan duration)
        {
            ConnectStatus status = ConnectStatus.Pending;
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < duration && status == ConnectStatus.Pending)
            {
                status = connector.Poll(out _);
                Thread.Sleep(1);
            }

            return status;
        }

        private static ITransport ConnectedTransport(ITransportConnector connector)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(10))
            {
                ConnectStatus status = connector.Poll(out ITransport transport);
                if (status == ConnectStatus.Connected)
                {
                    return transport;
                }

                Assert.AreEqual(ConnectStatus.Pending, status);
                Thread.Sleep(1);
            }

            Assert.Fail("the connector did not connect");
            return null;
        }

        private sealed class PinnedCheck : ITlsCertificateCheck
        {
            private readonly TestCertificate pinned;

            public PinnedCheck(TestCertificate pinned)
            {
                this.pinned = pinned;
            }

            public List<string> Hosts { get; } = new List<string>();

            public List<int> ChainLengths { get; } = new List<int>();

            public bool Accepts(string host, IReadOnlyList<byte[]> chain, out string rejection)
            {
                Hosts.Add(host);
                ChainLengths.Add(chain.Count);
                bool signedByPin = TestCertificatesAccess.IsSignedBy(chain[0], pinned);
                rejection = signedByPin ? null : "not pinned";
                return signedByPin;
            }
        }

        private sealed class FixedCheck : ITlsCertificateCheck
        {
            private readonly bool accepted;
            private readonly string rejection;
            private readonly Exception error;

            public FixedCheck(bool accepted, string rejection, Exception error)
            {
                this.accepted = accepted;
                this.rejection = rejection;
                this.error = error;
            }

            public bool Accepts(string host, IReadOnlyList<byte[]> chain, out string reason)
            {
                if (error != null)
                {
                    throw error;
                }

                reason = rejection;
                return accepted;
            }
        }

        private static class TestCertificatesAccess
        {
            public static bool IsSignedBy(byte[] der, TestCertificate issuer)
            {
                try
                {
                    new Org.BouncyCastle.X509.X509CertificateParser().ReadCertificate(der).Verify(issuer.Certificate.GetPublicKey());
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        private sealed class Rig : IDisposable
        {
            private readonly ClientReconnector reconnector;

            public Rig(ITransportFactory backend)
            {
                var channels = new MessageChannels();
                channels.Set(GreetingId, Channel.ReliableOrdered);
                var serverDispatcher = new MessageDispatcher(TestSchema());
                serverDispatcher.Register(GreetingId, (peerId, payload) => ServerReceived.Add(payload.ToArray()));
                Server = new ServerSession(TestSchema(), new SessionConfig(), new SessionLimits(), serverDispatcher, TestBundles.Protocol(channels, backend.FrameBudget)).SendingLocalPeer();
                Server.Start(backend.CreateListener(0, out ushort port));
                BackendPort = port;
                var clientDispatcher = new MessageDispatcher(TestSchema());
                clientDispatcher.Register(GreetingId, (peerId, payload) => ClientReceived.Add(payload.ToArray()));
                Client = new ClientSession(TestSchema(), new SessionConfig(), new SessionLimits(), clientDispatcher, TestBundles.Protocol(channels, backend.FrameBudget)).AcceptingLocalPeer();
                Client.OnTlsRejected += Rejections.Add;
                reconnector = new ClientReconnector(Client, new ReconnectPolicy { Interval = TimeSpan.FromMilliseconds(50) });
                reconnector.OnClientConnectionState += args =>
                {
                    if (args.State == ConnectionState.Stopped)
                    {
                        Stops.Add(args);
                    }
                };
            }

            public ServerSession Server { get; }

            public ClientSession Client { get; }

            public ushort BackendPort { get; }

            public List<byte[]> ServerReceived { get; } = new List<byte[]>();

            public List<byte[]> ClientReceived { get; } = new List<byte[]>();

            public List<ConnectionStateArgs> Stops { get; } = new List<ConnectionStateArgs>();

            public List<string> Rejections { get; } = new List<string>();

            public void Connect(Func<ITransportConnector> source) => reconnector.Start(source, MonotonicClock.Now);

            public void RunUntil(Func<bool> done)
            {
                var clock = Stopwatch.StartNew();
                while (!done() && clock.Elapsed < TimeSpan.FromSeconds(10))
                {
                    Step();
                }
            }

            public void Run(TimeSpan duration)
            {
                var clock = Stopwatch.StartNew();
                while (clock.Elapsed < duration)
                {
                    Step();
                }
            }

            public void Dispose()
            {
                reconnector.Stop();
                Server.Stop();
            }

            private void Step()
            {
                TimeSpan now = MonotonicClock.Now;
                Server.Tick(now);
                reconnector.Tick(now);
                Server.Flush();
                Client.Flush();
                Thread.Sleep(1);
            }
        }
    }
}
