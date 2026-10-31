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
    public sealed class EncryptedWebSocketTransportTest
    {
        private static readonly byte[] Key = Enumerable.Range(1, ConnectTokens.KeyLength).Select(value => (byte)value).ToArray();
        private static readonly byte[] Hello = Encoding.ASCII.GetBytes("a frame over a WebSocket message");
        private static readonly IPEndPoint WebSocketClient = new IPEndPoint(IPAddress.Parse("198.51.100.9"), 40000);

        private readonly List<IDisposable> owned = new List<IDisposable>();
        private ConnectTokens tokens;
        private EncryptedWebSocketTransportFactory clientFactory;
        private IListenerTransport pod;
        private ushort podPort;

        [SetUp]
        public void SetUp()
        {
            DateTime now = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
            tokens = new ConnectTokens(TestObjects.Registry(), Key, null, () => now);
            clientFactory = new EncryptedWebSocketTransportFactory(TestObjects.Registry());
            pod = Own(new EncryptedUdpTransportFactory(TestObjects.Registry(), tokens, "pod-a", true).CreateListener(0, out podPort));
        }

        [TearDown]
        public void TearDown()
        {
            for (int index = owned.Count - 1; index >= 0; index--)
            {
                owned[index].Dispose();
            }
        }

        [Test]
        public void AClientSendsEncryptedDatagramsAsMessagesAndThePodRepliesThroughTheGateway()
        {
            using (var gateway = new WebSocketGatewayStub(podPort))
            {
                ITransport client = Connect(tokens.Issue("pod-a", 77, new byte[] { 5 }), gateway);
                client.Send(Hello);
                ITransport peer = AcceptOne(gateway);
                CollectionAssert.AreEqual(Hello, ReceiveOne(peer, gateway));
                peer.Send(new byte[] { 1, 2, 3 });
                byte[] answer = ReceiveOne(client, gateway);
                client.Send(new byte[] { 4 });

                CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, answer);
                CollectionAssert.AreEqual(new byte[] { 4 }, ReceiveOne(peer, gateway));
                CollectionAssert.AreEqual(new byte[] { 5 }, ((IConnectionData)peer).ConnectionData.ToArray());
                Assert.IsTrue(((IGatewayArrival)peer).ArrivedOverWebSocket);
                Assert.AreEqual(0, gateway.FirstMessage[8]);
                Assert.AreEqual(77UL, BitConverter.ToUInt64(gateway.FirstMessage, 0));
                Assert.IsFalse(Encoding.ASCII.GetString(gateway.FirstMessage).Contains("WebSocket message"));
                Assert.IsTrue(gateway.EveryReplyOverWebSocket);
            }
        }

        [Test]
        public void TheFactoryOnlyConnectsWithAGrantAndHasTheBudgetOfEncryptedUdp()
        {
            Assert.AreEqual(new EncryptedUdpTransportFactory(TestObjects.Registry()).FrameBudget, clientFactory.FrameBudget);
            Assert.AreEqual(1175, clientFactory.FrameBudget);
            Assert.IsTrue(clientFactory.RequiresConnection);
            Assert.IsTrue(clientFactory.CanUseConnection(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty)));
            Assert.IsFalse(clientFactory.CanUseConnection(new byte[] { 1, 2 }));
            Assert.Throws<InvalidOperationException>(() => clientFactory.CreateListener(0, out _));
            Assert.Throws<InvalidOperationException>(() => clientFactory.CreateConnector("127.0.0.1", 80));
            Assert.Throws<ArgumentException>(() => clientFactory.CreateConnector("127.0.0.1", 80, new byte[] { 1, 2 }));
        }

        [Test]
        public void AFailedWebSocketConnectFailsTheConnector()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            ushort closedPort = (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            ITransportConnector connector = Own(clientFactory.CreateConnector("127.0.0.1", closedPort, tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty)));
            Stopwatch watch = Stopwatch.StartNew();
            ConnectStatus status;
            while ((status = connector.Poll(out _)) == ConnectStatus.Pending && watch.Elapsed < TimeSpan.FromSeconds(10))
            {
                Thread.Sleep(1);
            }

            Assert.AreEqual(ConnectStatus.Failed, status);
        }

        private ITransport Connect(byte[] grant, WebSocketGatewayStub gateway)
        {
            ITransportConnector connector = Own(clientFactory.CreateConnector("127.0.0.1", gateway.Port, grant));
            Stopwatch watch = Stopwatch.StartNew();
            ConnectStatus status;
            ITransport transport;
            while ((status = connector.Poll(out transport)) == ConnectStatus.Pending && watch.Elapsed < TimeSpan.FromSeconds(5))
            {
                gateway.Pump();
                Thread.Sleep(1);
            }

            Assert.AreEqual(ConnectStatus.Connected, status);
            return Own(transport);
        }

        private ITransport AcceptOne(WebSocketGatewayStub gateway)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(5))
            {
                gateway.Pump();
                AcceptOutcome outcome = pod.Accept();
                if (outcome.Status == AcceptStatus.Accepted)
                {
                    return Own(outcome.Transport);
                }

                Thread.Sleep(1);
            }

            return null;
        }

        private byte[] ReceiveOne(ITransport transport, WebSocketGatewayStub gateway)
        {
            var buffer = new byte[65536];
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(5))
            {
                gateway.Pump();
                pod.Accept();
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

        private sealed class WebSocketGatewayStub : IDisposable
        {
            private readonly IPEndPoint pod;
            private readonly IListenerTransport listener;
            private readonly UdpClient back = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            private readonly byte[] message = new byte[65536];
            private ITransport client;

            public WebSocketGatewayStub(ushort podPort)
            {
                pod = new IPEndPoint(IPAddress.Loopback, podPort);
                listener = new WebSocketTransportFactory().CreateListener(0, out ushort port);
                Port = port;
            }

            public ushort Port { get; }

            public byte[] FirstMessage { get; private set; }

            public bool EveryReplyOverWebSocket { get; private set; } = true;

            public void Pump()
            {
                AcceptOutcome accepted = listener.Accept();
                if (accepted.Status == AcceptStatus.Accepted)
                {
                    client = accepted.Transport;
                }

                while (client != null)
                {
                    ReceiveOutcome outcome = client.Receive(message);
                    if (outcome.Signal != TransportSignal.Ok)
                    {
                        break;
                    }

                    FirstMessage ??= message.Take(outcome.Count).ToArray();
                    var wrapped = new byte[GatewayEnvelope.Length + outcome.Count];
                    GatewayEnvelope.Write(wrapped, WebSocketClient, true);
                    Buffer.BlockCopy(message, 0, wrapped, GatewayEnvelope.Length, outcome.Count);
                    back.Send(wrapped, wrapped.Length, pod);
                }

                while (back.Available > 0)
                {
                    IPEndPoint from = null;
                    byte[] bytes = back.Receive(ref from);
                    Assert.IsTrue(GatewayEnvelope.TryRead(bytes, bytes.Length, out IPEndPoint addressed, out bool overWebSocket));
                    EveryReplyOverWebSocket &= overWebSocket && addressed.Equals(WebSocketClient);
                    client?.Send(new ReadOnlySpan<byte>(bytes, GatewayEnvelope.Length, bytes.Length - GatewayEnvelope.Length));
                }
            }

            public void Dispose()
            {
                client?.Dispose();
                listener.Dispose();
                back.Dispose();
            }
        }
    }
}
