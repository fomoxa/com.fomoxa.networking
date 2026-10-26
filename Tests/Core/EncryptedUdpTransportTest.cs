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
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class EncryptedUdpTransportTest
    {
        private static readonly byte[] Key = Enumerable.Range(1, ConnectTokens.KeyLength).Select(value => (byte)value).ToArray();
        private static readonly byte[] Hello = Encoding.ASCII.GetBytes("a frame the client sends first");

        private readonly List<IDisposable> owned = new List<IDisposable>();
        private DateTime now;
        private ConnectTokens tokens;
        private EncryptedUdpTransportFactory serverFactory;
        private EncryptedUdpTransportFactory clientFactory;
        private IListenerTransport listener;
        private ushort port;

        [SetUp]
        public void SetUp()
        {
            now = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
            tokens = new ConnectTokens(TestObjects.Registry(), Key, null, () => now);
            serverFactory = new EncryptedUdpTransportFactory(TestObjects.Registry(), tokens, "pod-a");
            clientFactory = new EncryptedUdpTransportFactory(TestObjects.Registry());
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
        public void AClientWithAGrantReachesTheServerAndBothSidesExchangeFrames()
        {
            byte[] grant = tokens.Issue("pod-a", 77, new byte[] { 5 });
            ITransport client = Connect(grant, port);

            Assert.AreEqual(SendOutcome.Ok.Signal, client.Send(Hello).Signal);
            ITransport peer = AcceptOne();
            CollectionAssert.AreEqual(Hello, ReceiveOne(peer));
            CollectionAssert.AreEqual(new byte[] { 5 }, ((IConnectionData)peer).ConnectionData.ToArray());

            peer.Send(new byte[] { 1, 2, 3 });
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, ReceiveOne(client));
            client.Send(new byte[] { 4 });
            CollectionAssert.AreEqual(new byte[] { 4 }, ReceiveOne(peer));
        }

        [Test]
        public void TheWireCarriesTheSessionIdAndTokenInTheClearAndTheFrameSealed()
        {
            using (var fakeServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            {
                byte[] grant = tokens.Issue("pod-a", 0x1122334455667788, ReadOnlyMemory<byte>.Empty);
                ITransport client = Connect(grant, (ushort)((IPEndPoint)fakeServer.Client.LocalEndPoint).Port);
                client.Send(Hello);
                client.Send(Hello);
                IPEndPoint from = null;
                byte[] first = fakeServer.Receive(ref from);
                byte[] second = fakeServer.Receive(ref from);
                byte[] token = GrantOf(grant).Token.ToArray();

                Assert.AreEqual(0x1122334455667788UL, BitConverter.ToUInt64(first, 0));
                Assert.AreEqual(0, first[8]);
                Assert.AreEqual(0UL, BitConverter.ToUInt64(first, 9));
                Assert.AreEqual(1UL, BitConverter.ToUInt64(second, 9));
                Assert.AreEqual(token.Length, BitConverter.ToUInt16(first, 17));
                CollectionAssert.AreEqual(token, first.Skip(19).Take(token.Length).ToArray());
                Assert.AreEqual(19 + token.Length + 16 + Hello.Length + 16, first.Length);
                Assert.IsFalse(Encoding.ASCII.GetString(first).Contains("client sends"));
                CollectionAssert.AreEqual(first.Skip(19 + token.Length).Take(16), second.Skip(19 + token.Length).Take(16));
                CollectionAssert.AreNotEqual(first.Skip(35 + token.Length), second.Skip(35 + token.Length));
            }
        }

        [Test]
        public void EachConnectionAttemptPicksItsOwnAttemptBytes()
        {
            using (var fakeServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            {
                byte[] grant = tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty);
                ushort fakePort = (ushort)((IPEndPoint)fakeServer.Client.LocalEndPoint).Port;
                Connect(grant, fakePort).Send(Hello);
                Connect(grant, fakePort).Send(Hello);
                IPEndPoint from = null;
                byte[] first = fakeServer.Receive(ref from);
                byte[] second = fakeServer.Receive(ref from);
                int attemptOffset = 19 + GrantOf(grant).Token.Length;

                CollectionAssert.AreNotEqual(first.Skip(attemptOffset).Take(16), second.Skip(attemptOffset).Take(16));
                CollectionAssert.AreNotEqual(first.Skip(attemptOffset + 16), second.Skip(attemptOffset + 16));
            }
        }

        [Test]
        public void ATokenForAnotherTargetOpensNoPeer()
        {
            ITransport client = Connect(tokens.Issue("pod-b", 77, ReadOnlyMemory<byte>.Empty), port);
            client.Send(Hello);

            Assert.IsNull(AcceptOne(TimeSpan.FromMilliseconds(200)));
        }

        [Test]
        public void AnExpiredTokenOpensNoPeer()
        {
            ITransport client = Connect(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty), port);
            now = now.AddMinutes(1);
            client.Send(Hello);

            Assert.IsNull(AcceptOne(TimeSpan.FromMilliseconds(200)));
        }

        [Test]
        public void AFirstDatagramWithAChangedFrameOpensNoPeerAndLeavesItsAttemptUsed()
        {
            using (var relay = new Relay(port))
            {
                relay.Change = bytes => bytes[bytes.Length - 1] ^= 1;
                byte[] grant = tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty);
                ITransport client = Connect(grant, relay.Port);
                client.Send(Hello);
                relay.Pump();

                Assert.IsNull(AcceptOne(TimeSpan.FromMilliseconds(200)));
                Assert.AreEqual(1, ((MemoryUsedTicketStore)tokens.UsedAttempts).Count);
            }
        }

        [Test]
        public void AReplayedFirstDatagramOpensNoSecondPeerAndDeliversNothingTwice()
        {
            using (var relay = new Relay(port))
            {
                ITransport client = Connect(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty), relay.Port);
                client.Send(Hello);
                relay.Pump();
                ITransport peer = AcceptOne();
                CollectionAssert.AreEqual(Hello, ReceiveOne(peer));

                relay.Replay(0);
                relay.ReplayFromAnotherAddress(0);

                Assert.IsNull(AcceptOne(TimeSpan.FromMilliseconds(200)));
                Assert.IsNull(ReceiveOne(peer, TimeSpan.FromMilliseconds(100)));
            }
        }

        [Test]
        public void AReplayedDataDatagramIsDeliveredOnceAndDoesNotMoveThePeer()
        {
            using (var relay = new Relay(port))
            {
                ITransport client = Connect(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty), relay.Port);
                client.Send(Hello);
                relay.Pump();
                ITransport peer = AcceptOne();
                ReceiveOne(peer);
                peer.Send(new byte[] { 1 });
                relay.Pump();
                ReceiveOne(client);
                client.Send(new byte[] { 9 });
                relay.Pump();
                CollectionAssert.AreEqual(new byte[] { 9 }, ReceiveOne(peer));

                relay.Replay(1);
                relay.ReplayFromAnotherAddress(1);
                Assert.IsNull(ReceiveOne(peer, TimeSpan.FromMilliseconds(100)));

                peer.Send(new byte[] { 2 });
                relay.Pump();
                CollectionAssert.AreEqual(new byte[] { 2 }, ReceiveOne(client));
            }
        }

        [Test]
        public void ADatagramFromANewAddressThatOpensMovesThePeer()
        {
            using (var relay = new Relay(port))
            {
                ITransport client = Connect(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty), relay.Port);
                client.Send(Hello);
                relay.Pump();
                ITransport peer = AcceptOne();
                ReceiveOne(peer);
                peer.Send(new byte[] { 1 });
                relay.Pump();
                ReceiveOne(client);

                relay.MoveToAnotherAddress();
                client.Send(new byte[] { 3 });
                relay.Pump();
                CollectionAssert.AreEqual(new byte[] { 3 }, ReceiveOne(peer));
                peer.Send(new byte[] { 4 });
                relay.Pump();

                CollectionAssert.AreEqual(new byte[] { 4 }, ReceiveOne(client));
            }
        }

        [Test]
        public void TheClientTakesServerDatagramsOnlyFromTheServerAddress()
        {
            using (var relay = new Relay(port))
            {
                ITransport client = Connect(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty), relay.Port);
                client.Send(Hello);
                relay.Pump();
                ITransport peer = AcceptOne();
                relay.HoldToClient = true;
                peer.Send(new byte[] { 1 });
                relay.Pump();

                relay.SendToClientFromAnotherAddress(0);
                Assert.IsNull(ReceiveOne(client, TimeSpan.FromMilliseconds(100)));
                relay.ReplayToClient(0);
                CollectionAssert.AreEqual(new byte[] { 1 }, ReceiveOne(client));
            }
        }

        [Test]
        public void AServerDatagramWithAChangedByteIsDropped()
        {
            using (var relay = new Relay(port))
            {
                ITransport client = Connect(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty), relay.Port);
                client.Send(Hello);
                relay.Pump();
                ITransport peer = AcceptOne();
                relay.ChangeToClient = bytes => bytes[bytes.Length - 1] ^= 1;
                peer.Send(new byte[] { 1 });
                relay.Pump();

                Assert.IsNull(ReceiveOne(client, TimeSpan.FromMilliseconds(100)));
            }
        }

        [Test]
        public void AfterTheServerAnswersTheClientSendsDataDatagramsWithoutTheToken()
        {
            using (var relay = new Relay(port))
            {
                ITransport client = Connect(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty), relay.Port);
                client.Send(Hello);
                relay.Pump();
                ITransport peer = AcceptOne();
                ReceiveOne(peer);
                peer.Send(new byte[] { 1 });
                relay.Pump();
                ReceiveOne(client);
                client.Send(new byte[] { 9, 9 });
                relay.Pump();

                Assert.AreEqual(1, relay.ClientDatagram(1)[8]);
                Assert.AreEqual(17 + 2 + 16, relay.ClientDatagram(1).Length);
                CollectionAssert.AreEqual(new byte[] { 9, 9 }, ReceiveOne(peer));
            }
        }

        [Test]
        public void AClientDatagramReflectedBackToTheClientIsDropped()
        {
            using (var relay = new Relay(port))
            {
                ITransport client = Connect(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty), relay.Port);
                client.Send(Hello);
                relay.Pump();
                ITransport peer = AcceptOne();
                peer.Send(new byte[] { 1 });
                relay.Pump();
                ReceiveOne(client);
                client.Send(new byte[] { 9 });
                relay.Pump();

                relay.ReflectToClient(1);

                Assert.IsNull(ReceiveOne(client, TimeSpan.FromMilliseconds(100)));
            }
        }

        [Test]
        public void AReplayedServerDatagramIsDeliveredToTheClientOnce()
        {
            using (var relay = new Relay(port))
            {
                ITransport client = Connect(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty), relay.Port);
                client.Send(Hello);
                relay.Pump();
                ITransport peer = AcceptOne();
                peer.Send(new byte[] { 1 });
                relay.Pump();
                CollectionAssert.AreEqual(new byte[] { 1 }, ReceiveOne(client));

                relay.ReplayToClient(0);

                Assert.IsNull(ReceiveOne(client, TimeSpan.FromMilliseconds(100)));
            }
        }

        [Test]
        public void AChangedDatagramDoesNotUseUpItsSequenceNumber()
        {
            using (var relay = new Relay(port))
            {
                ITransport client = Connect(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty), relay.Port);
                client.Send(Hello);
                relay.Pump();
                ITransport peer = AcceptOne();
                ReceiveOne(peer);
                peer.Send(new byte[] { 1 });
                relay.Pump();
                ReceiveOne(client);
                relay.Change = bytes => bytes[bytes.Length - 1] ^= 1;
                client.Send(new byte[] { 9 });
                relay.Pump();
                Assert.IsNull(ReceiveOne(peer, TimeSpan.FromMilliseconds(100)));

                relay.Replay(1);

                CollectionAssert.AreEqual(new byte[] { 9 }, ReceiveOne(peer));
            }
        }

        [Test]
        public void AFirstDatagramWhoseSessionIdIsNotTheTokensOpensNoPeer()
        {
            using (var sender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            {
                var server = new IPEndPoint(IPAddress.Loopback, port);
                byte[] moved = ConnectDatagram(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty), 78);
                sender.Send(moved, moved.Length, server);
                Assert.IsNull(AcceptOne(TimeSpan.FromMilliseconds(200)));

                byte[] matching = ConnectDatagram(tokens.Issue("pod-a", 78, ReadOnlyMemory<byte>.Empty), 78);
                sender.Send(matching, matching.Length, server);
                Assert.IsNotNull(AcceptOne());
            }
        }

        [Test]
        public void FramesSentBeforeTheServerAnswersAllReachThePeer()
        {
            ITransport client = Connect(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty), port);
            client.Send(Hello);
            client.Send(new byte[] { 2 });
            ITransport peer = AcceptOne();

            CollectionAssert.AreEqual(Hello, ReceiveOne(peer));
            CollectionAssert.AreEqual(new byte[] { 2 }, ReceiveOne(peer));
        }

        [Test]
        public void TheListenerHoldsAtMostItsCeilingOfSessions()
        {
            using (var sender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            {
                var server = new IPEndPoint(IPAddress.Loopback, port);
                for (ulong sessionId = 1; sessionId <= EncryptedUdpListener.PeerCeiling; sessionId++)
                {
                    byte[] datagram = ConnectDatagram(tokens.Issue("pod-a", sessionId, ReadOnlyMemory<byte>.Empty), sessionId);
                    sender.Send(datagram, datagram.Length, server);
                    Assert.IsNotNull(AcceptOne(), $"session {sessionId}");
                }

                ulong overCeiling = EncryptedUdpListener.PeerCeiling + 1;
                byte[] last = ConnectDatagram(tokens.Issue("pod-a", overCeiling, ReadOnlyMemory<byte>.Empty), overCeiling);
                sender.Send(last, last.Length, server);
                Assert.IsNull(AcceptOne(TimeSpan.FromMilliseconds(200)));
            }
        }

        [Test]
        public void ANewAttemptWithTheSameTokenReplacesThePeerOfTheEarlierAttempt()
        {
            byte[] grant = tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty);
            Connect(grant, port).Send(Hello);
            ITransport earlier = AcceptOne();
            ReceiveOne(earlier);

            ITransport retry = Connect(grant, port);
            retry.Send(Hello);
            ITransport later = AcceptOne();

            Assert.IsNotNull(later);
            Assert.AreEqual(TransportSignal.Closed, earlier.Receive(new byte[64]).Signal);
            Assert.AreEqual(TransportSignal.Closed, earlier.Send(new byte[] { 1 }).Signal);
            CollectionAssert.AreEqual(Hello, ReceiveOne(later));
            earlier.Dispose();
            later.Send(new byte[] { 6 });
            CollectionAssert.AreEqual(new byte[] { 6 }, ReceiveOne(retry));
            retry.Send(new byte[] { 7 });
            CollectionAssert.AreEqual(new byte[] { 7 }, ReceiveOne(later));
        }

        [Test]
        public void AnotherTokenWithASessionIdInUseOpensNoPeerAndLeavesTheFirstAlone()
        {
            Connect(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty), port).Send(Hello);
            ITransport first = AcceptOne();
            ReceiveOne(first);

            Connect(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty), port).Send(Hello);

            Assert.IsNull(AcceptOne(TimeSpan.FromMilliseconds(200)));
            Assert.AreEqual(TransportSignal.WouldBlock, first.Receive(new byte[64]).Signal);
        }

        [Test]
        public void ADisposedPeerFreesItsSessionId()
        {
            byte[] grant = tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty);
            Connect(grant, port).Send(Hello);
            AcceptOne().Dispose();

            Connect(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty), port).Send(Hello);

            Assert.IsNotNull(AcceptOne());
        }

        [Test]
        public void AFrameTooLargeForADatagramIsRefused()
        {
            ITransport client = Connect(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty), port);

            Assert.AreEqual(TransportSignal.TooLarge, client.Send(new byte[65507]).Signal);
        }

        [Test]
        public void TheFrameBudgetKeepsADataDatagramAtTwelveHundredAndEightBytes()
        {
            Assert.AreEqual(1208 - 17 - 16, clientFactory.FrameBudget);
        }

        [Test]
        public void TheFactoryNeedsAConnectionToConnectAndTokensToListen()
        {
            Assert.IsTrue(clientFactory.RequiresConnection);
            Assert.Throws<InvalidOperationException>(() => clientFactory.CreateConnector("127.0.0.1", port));
            Assert.Throws<InvalidOperationException>(() => clientFactory.CreateListener(0, out _));
            Assert.Throws<ArgumentException>(() => clientFactory.CreateConnector("127.0.0.1", port, new byte[] { 1, 2 }));
            Assert.IsFalse(clientFactory.CanUseConnection(ReadOnlyMemory<byte>.Empty));
            Assert.IsTrue(clientFactory.CanUseConnection(tokens.Issue("pod-a", ReadOnlyMemory<byte>.Empty)));
        }

        [Test]
        public void SessionKeysAreHkdfSha256OfTheTokenKeyWithTheAttemptAsSalt()
        {
            byte[] tokenKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
            byte[] attempt = Enumerable.Range(0xA0, 16).Select(value => (byte)value).ToArray();

            Assert.AreEqual("93ca9570669bd7e9be2df9a4ec962ed25e7241def3045136331249f3e2eff0c0", Hex(DatagramCipher.DeriveClientKey(tokenKey, attempt)));
            Assert.AreEqual("2bb059829404dd8bc2204967ab46ed34e32930ac975b289a0867b9f8540f20d5", Hex(DatagramCipher.DeriveServerKey(tokenKey, attempt)));
        }

        [Test]
        public void ReplayWindowForgetsEverythingBehindAJumpOfSixtyFourOrMore()
        {
            var window = new ReplayWindow();
            window.Mark(0);
            window.Mark(1);
            window.Mark(65);

            Assert.IsTrue(window.IsFresh(64));
            Assert.IsTrue(window.IsFresh(2));
            Assert.IsFalse(window.IsFresh(1));
            Assert.IsFalse(window.IsFresh(65));
        }

        [Test]
        public void ReplayWindowAcceptsEachSequenceOnceWithinSixtyFourBehindTheHighest()
        {
            var window = new ReplayWindow();
            Assert.IsTrue(window.IsFresh(5));
            window.Mark(5);
            Assert.IsFalse(window.IsFresh(5));
            Assert.IsTrue(window.IsFresh(3));
            window.Mark(3);
            Assert.IsFalse(window.IsFresh(3));
            window.Mark(100);
            Assert.IsTrue(window.IsFresh(37));
            Assert.IsFalse(window.IsFresh(36));
            Assert.IsFalse(window.IsFresh(5));
            window.Mark(37);
            Assert.IsFalse(window.IsFresh(37));
            Assert.IsTrue(window.IsFresh(99));
            window.Mark(101);
            Assert.IsFalse(window.IsFresh(100));
            Assert.IsFalse(window.IsFresh(37));
            Assert.IsTrue(window.IsFresh(38));
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

        private byte[] ReceiveOne(ITransport transport) => ReceiveOne(transport, TimeSpan.FromSeconds(2));

        private byte[] ReceiveOne(ITransport transport, TimeSpan timeout)
        {
            var buffer = new byte[65536];
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.Elapsed < timeout)
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

        private static byte[] ConnectDatagram(byte[] issued, ulong sessionId)
        {
            ConnectionGrant grant = GrantOf(issued);
            byte[] token = grant.Token.ToArray();
            byte[] attempt = Guid.NewGuid().ToByteArray();
            var key = new Org.BouncyCastle.Crypto.Parameters.KeyParameter(DatagramCipher.DeriveClientKey(grant.ClientKey.ToArray(), attempt));
            int headerSize = 19 + token.Length + 16;
            var datagram = new byte[headerSize + Hello.Length + 16];
            EncryptedDatagram.WriteConnectHeader(datagram, sessionId, 0, token, attempt);
            new DatagramCipher().Seal(key, 0, datagram, headerSize, Hello, 0, Hello.Length, datagram, headerSize);
            return datagram;
        }

        private static string Hex(byte[] bytes) => string.Concat(bytes.Select(value => value.ToString("x2")));

        private static ConnectionGrant GrantOf(byte[] issued)
        {
            var grant = new ConnectionGrant();
            ConnectionGrantNetAdapter.Instance.Decode(issued, ref grant);
            return grant;
        }

        private sealed class Relay : IDisposable
        {
            private readonly IPEndPoint server;
            private readonly UdpClient front = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            private readonly List<byte[]> fromClient = new List<byte[]>();
            private readonly List<byte[]> fromServer = new List<byte[]>();
            private UdpClient back = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            private IPEndPoint client;

            public Relay(ushort serverPort)
            {
                server = new IPEndPoint(IPAddress.Loopback, serverPort);
            }

            public ushort Port => (ushort)((IPEndPoint)front.Client.LocalEndPoint).Port;

            public Action<byte[]> Change { get; set; }

            public Action<byte[]> ChangeToClient { get; set; }

            public bool HoldToClient { get; set; }

            public void Pump()
            {
                Thread.Sleep(20);
                while (front.Available > 0)
                {
                    IPEndPoint from = null;
                    byte[] bytes = front.Receive(ref from);
                    client = from;
                    fromClient.Add((byte[])bytes.Clone());
                    Change?.Invoke(bytes);
                    back.Send(bytes, bytes.Length, server);
                }

                while (back.Available > 0)
                {
                    IPEndPoint from = null;
                    byte[] bytes = back.Receive(ref from);
                    fromServer.Add((byte[])bytes.Clone());
                    ChangeToClient?.Invoke(bytes);
                    if (!HoldToClient)
                    {
                        front.Send(bytes, bytes.Length, client);
                    }
                }

                Thread.Sleep(20);
            }

            public void Replay(int index) => back.Send(fromClient[index], fromClient[index].Length, server);

            public byte[] ClientDatagram(int index) => fromClient[index];

            public void ReflectToClient(int index) => front.Send(fromClient[index], fromClient[index].Length, client);

            public void ReplayToClient(int index) => front.Send(fromServer[index], fromServer[index].Length, client);

            public void SendToClientFromAnotherAddress(int index)
            {
                using (var other = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                {
                    other.Send(fromServer[index], fromServer[index].Length, client);
                }
            }

            public void ReplayFromAnotherAddress(int index)
            {
                using (var other = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                {
                    other.Send(fromClient[index], fromClient[index].Length, server);
                }
            }

            public void MoveToAnotherAddress()
            {
                back.Dispose();
                back = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            }

            public void Dispose()
            {
                front.Dispose();
                back.Dispose();
            }
        }
    }
}
