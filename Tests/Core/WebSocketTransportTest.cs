using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class WebSocketTransportTest
    {
        private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

        [Test]
        public void TwoEndsHandshakeAndCarryMessagesOfEveryLengthEncoding()
        {
            var factory = new WebSocketTransportFactory();
            using IListenerTransport listener = factory.CreateListener(0, out ushort port);
            using ITransportConnector connector = factory.CreateConnector("127.0.0.1", port);
            (ITransport client, ITransport server) = Connect(connector, listener);
            using (client)
            using (server)
            {
                foreach (int length in new[] { 0, 1, 125, 126, 65535, 65536, 200_000 })
                {
                    byte[] message = Pattern(length);
                    Assert.AreEqual(TransportSignal.Ok, SendAll(client, message).Signal);
                    CollectionAssert.AreEqual(message, ReceiveOne(server, client));
                    Assert.AreEqual(TransportSignal.Ok, SendAll(server, message).Signal);
                    CollectionAssert.AreEqual(message, ReceiveOne(client, server));
                }

                Assert.AreEqual(TransportKind.Message, client.Kind);
                Assert.AreEqual(FomoxaWire.MaxDataFrameSize, factory.FrameBudget);
            }
        }

        [Test]
        public void ASmallBufferGetsTheNeededCapacityAndTheMessageIsKept()
        {
            var factory = new WebSocketTransportFactory();
            using IListenerTransport listener = factory.CreateListener(0, out ushort port);
            using ITransportConnector connector = factory.CreateConnector("127.0.0.1", port);
            (ITransport client, ITransport server) = Connect(connector, listener);
            using (client)
            using (server)
            {
                byte[] message = Pattern(300);
                SendAll(client, message);
                ReceiveOutcome outcome = Poll(() => server.Receive(new byte[10]), client);

                Assert.AreEqual(TransportSignal.NeedCapacity, outcome.Signal);
                Assert.AreEqual(300, outcome.Count);
                CollectionAssert.AreEqual(message, ReceiveOne(server, client));
            }
        }

        [Test]
        public void AGracefulCloseIsAnsweredAndBothEndsSeeClosed()
        {
            var factory = new WebSocketTransportFactory();
            using IListenerTransport listener = factory.CreateListener(0, out ushort port);
            using ITransportConnector connector = factory.CreateConnector("127.0.0.1", port);
            (ITransport client, ITransport server) = Connect(connector, listener);
            using (client)
            using (server)
            {
                client.CloseGracefully();

                Assert.AreEqual(TransportSignal.Closed, client.Send(new byte[1]).Signal);
                Assert.AreEqual(TransportSignal.Closed, Poll(() => server.Receive(new byte[16]), client).Signal);
                Assert.AreEqual(TransportSignal.Closed, Poll(() => client.Receive(new byte[16]), server).Signal);
            }
        }

        [Test]
        public void AMessageOverTheCeilingIsTooLargeAndTheConnectionStaysUp()
        {
            var factory = new WebSocketTransportFactory();
            using IListenerTransport listener = factory.CreateListener(0, out ushort port);
            using ITransportConnector connector = factory.CreateConnector("127.0.0.1", port);
            (ITransport client, ITransport server) = Connect(connector, listener);
            using (client)
            using (server)
            {
                Assert.AreEqual(TransportSignal.TooLarge, client.Send(new byte[WebSocketTransport.MaxMessageBytes + 1]).Signal);
                SendAll(client, Pattern(3));
                CollectionAssert.AreEqual(Pattern(3), ReceiveOne(server, client));
            }
        }

        [Test]
        public void TheDotNetClientWebSocketTalksToTheListener()
        {
            var factory = new WebSocketTransportFactory(new WebSocketSettings { Path = "/game" });
            using IListenerTransport listener = factory.CreateListener(0, out ushort port);
            using var dotnet = new ClientWebSocket();
            Task connecting = dotnet.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/game?room=1"), CancellationToken.None);
            ITransport server = AcceptWhile(listener, () => !connecting.IsCompleted);
            connecting.Wait(Limit);
            using (server)
            {
                Task sending = dotnet.SendAsync(new ArraySegment<byte>(Pattern(40)), WebSocketMessageType.Binary, false, CancellationToken.None);
                sending.Wait(Limit);
                sending = dotnet.SendAsync(new ArraySegment<byte>(Pattern(60).Skip(40).ToArray()), WebSocketMessageType.Binary, true, CancellationToken.None);
                sending.Wait(Limit);
                CollectionAssert.AreEqual(Pattern(60), ReceiveOne(server, null));

                SendAll(server, Pattern(70_000));
                var received = new byte[80_000];
                int total = 0;
                WebSocketReceiveResult result;
                do
                {
                    Task<WebSocketReceiveResult> receiving = dotnet.ReceiveAsync(new ArraySegment<byte>(received, total, received.Length - total), CancellationToken.None);
                    while (!receiving.IsCompleted)
                    {
                        server.Receive(new byte[16]);
                        Thread.Sleep(1);
                    }

                    result = receiving.Result;
                    total += result.Count;
                }
                while (!result.EndOfMessage);

                Assert.AreEqual(WebSocketMessageType.Binary, result.MessageType);
                CollectionAssert.AreEqual(Pattern(70_000), received.Take(total).ToArray());

                dotnet.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("text")), WebSocketMessageType.Text, true, CancellationToken.None).Wait(Limit);
                Assert.AreEqual(TransportSignal.Error, Poll(() => server.Receive(new byte[16]), null).Signal);
            }
        }

        [Test]
        public void TheConnectorTalksToADotNetServerWebSocket()
        {
            using var owned = new StartedListener();
            TcpListener tcp = owned.Listener;
            ushort port = (ushort)((IPEndPoint)tcp.LocalEndpoint).Port;
            using ITransportConnector connector = new WebSocketTransportFactory().CreateConnector($"ws://127.0.0.1:{port}/play", 1);
            Task<TcpClient> accepting = tcp.AcceptTcpClientAsync();
            PollConnector(connector, () => !accepting.IsCompleted, out _);
            using TcpClient accepted = accepting.Result;
            NetworkStream stream = accepted.GetStream();
            string request = ReadHeader(stream, connector);
            StringAssert.StartsWith("GET /play HTTP/1.1\r\n", request);
            StringAssert.Contains($"Host: 127.0.0.1:{port}\r\n", request);
            string key = request.Split(new[] { "\r\n" }, StringSplitOptions.None).First(line => line.StartsWith("Sec-WebSocket-Key: ")).Substring(19);
            byte[] response = Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {WebSocketHandshake.AcceptFor(key)}\r\n\r\n");
            stream.Write(response, 0, response.Length);
            Assert.AreEqual(ConnectStatus.Connected, PollConnector(connector, () => true, out ITransport client));
            using (client)
            using (WebSocket dotnet = WebSocket.CreateFromStream(stream, true, null, Timeout.InfiniteTimeSpan))
            {
                SendAll(client, Pattern(500));
                var received = new byte[600];
                Task<WebSocketReceiveResult> receiving = dotnet.ReceiveAsync(new ArraySegment<byte>(received), CancellationToken.None);
                Assert.IsTrue(receiving.Wait(Limit));
                Assert.AreEqual(500, receiving.Result.Count);
                CollectionAssert.AreEqual(Pattern(500), received.Take(500).ToArray());

                dotnet.SendAsync(new ArraySegment<byte>(Pattern(9)), WebSocketMessageType.Binary, true, CancellationToken.None).Wait(Limit);
                CollectionAssert.AreEqual(Pattern(9), ReceiveOne(client, null));

                Task closing = dotnet.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
                Assert.AreEqual(TransportSignal.Closed, Poll(() => client.Receive(new byte[16]), null).Signal);
                Assert.IsTrue(closing.Wait(Limit));
            }
        }

        [Test]
        public void APingIsAnsweredWithAPongAndAnUnmaskedClientFrameIsAnError()
        {
            var factory = new WebSocketTransportFactory();
            using IListenerTransport listener = factory.CreateListener(0, out ushort port);
            using var raw = new RawClient(port, "/");
            ITransport server = AcceptWhile(listener, () => !raw.Upgraded(listener));
            using (server)
            {
                raw.Send(0x9, true, Encoding.ASCII.GetBytes("hi"));
                Poll(() => server.Receive(new byte[16]), null, signal => raw.Available);
                (byte opcode, byte[] payload) = raw.ReadFrame();
                Assert.AreEqual(0xA, opcode);
                CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("hi"), payload);

                raw.Send(0x2, false, new byte[] { 1 });
                Assert.AreEqual(TransportSignal.Error, Poll(() => server.Receive(new byte[16]), null).Signal);
            }
        }

        [Test]
        public void TheListenerRejectsAWrongPathAnOriginOutsideTheListAndAnOldVersion()
        {
            var settings = new WebSocketSettings { Path = "/fomoxa" };
            settings.AllowedOrigins.Add("https://game.example");
            var factory = new WebSocketTransportFactory(settings);
            using IListenerTransport listener = factory.CreateListener(0, out ushort port);

            Assert.AreEqual("404", Rejected(listener, port, "GET /other HTTP/1.1\r\nHost: x\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n\r\n"));
            Assert.AreEqual("403", Rejected(listener, port, "GET /fomoxa HTTP/1.1\r\nHost: x\r\nOrigin: https://evil.example\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n\r\n"));
            Assert.AreEqual("426", Rejected(listener, port, "GET /fomoxa HTTP/1.1\r\nHost: x\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 8\r\n\r\n"));
            Assert.AreEqual("400", Rejected(listener, port, "GET /fomoxa HTTP/1.1\r\nHost: x\r\nConnection: Upgrade\r\nSec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n\r\n"));
            Assert.AreEqual("431", Rejected(listener, port, "GET /fomoxa HTTP/1.1\r\nHost: " + new string('x', 9000)));
        }

        [Test]
        public void AnAllowedOriginAndAMissingOriginAreAccepted()
        {
            var settings = new WebSocketSettings();
            settings.AllowedOrigins.Add("https://game.example");
            var factory = new WebSocketTransportFactory(settings);
            using IListenerTransport listener = factory.CreateListener(0, out ushort port);

            using (var browser = new RawClient(port, "/", "https://game.example"))
            {
                AcceptWhile(listener, () => !browser.Upgraded(listener)).Dispose();
            }

            using ITransportConnector connector = factory.CreateConnector("localhost", port);
            (ITransport client, ITransport server) = Connect(connector, listener);
            client.Dispose();
            server.Dispose();
        }

        [Test]
        public void AHandshakeThatDoesNotFinishInTimeIsDropped()
        {
            var now = TimeSpan.Zero;
            var settings = new WebSocketSettings { HandshakeTimeout = TimeSpan.FromSeconds(5), Clock = () => now };
            using var listener = (WebSocketListener)new WebSocketTransportFactory(settings).CreateListener(0, out ushort port);
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(IPAddress.Loopback, port);
            socket.Send(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\n"));
            Wait(() => listener.Accept().Status == AcceptStatus.Progress || listener.PendingCount == 1);
            Assert.AreEqual(1, listener.PendingCount);

            now = TimeSpan.FromSeconds(6);
            listener.Accept();

            Assert.AreEqual(0, listener.PendingCount);
            socket.ReceiveTimeout = 5000;
            Assert.AreEqual(0, socket.Receive(new byte[16]));
        }

        [Test]
        public void TheConnectorFailsOnAWrongAcceptKeyAndOnANonUpgradeStatus()
        {
            Assert.AreEqual(ConnectStatus.Failed, ConnectToScriptedServer(key => $"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {WebSocketHandshake.AcceptFor("other")}\r\n\r\n"));
            Assert.AreEqual(ConnectStatus.Failed, ConnectToScriptedServer(key => "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\n\r\n"));
        }

        [Test]
        public void AddressesAreHostsOrWsUrlsAndWssIsNotSupported()
        {
            WebSocketTransportFactory.Endpoint byHost = WebSocketTransportFactory.Parse("game.example", 7777, "/ws");
            Assert.AreEqual(("game.example", (ushort)7777, "/ws", "game.example:7777"), (byHost.Host, byHost.Port, byHost.Target, byHost.HostHeader));
            WebSocketTransportFactory.Endpoint byIpv6 = WebSocketTransportFactory.Parse("::1", 7777, "/");
            Assert.AreEqual("[::1]:7777", byIpv6.HostHeader);
            WebSocketTransportFactory.Endpoint byUrl = WebSocketTransportFactory.Parse("ws://game.example/fomoxa?room=2", 7777, "/ws");
            Assert.AreEqual(("game.example", (ushort)80, "/fomoxa?room=2", "game.example"), (byUrl.Host, byUrl.Port, byUrl.Target, byUrl.HostHeader));
            WebSocketTransportFactory.Endpoint withPort = WebSocketTransportFactory.Parse("WS://10.0.0.2:9000/", 1, "/ws");
            Assert.AreEqual(("10.0.0.2", (ushort)9000, "/", "10.0.0.2:9000"), (withPort.Host, withPort.Port, withPort.Target, withPort.HostHeader));
            Assert.Throws<NotSupportedException>(() => new WebSocketTransportFactory().CreateConnector("wss://game.example/", 1));
            Assert.AreEqual("ws://[::1]:7777/ws", WebSocketTransportFactory.UrlFor("::1", 7777, "/ws"));
            Assert.AreEqual("wss://game.example/fomoxa", WebSocketTransportFactory.UrlFor("wss://game.example/fomoxa", 7777, "/ws"));
            Assert.Throws<ArgumentException>(() => WebSocketTransportFactory.UrlFor("wss://", 1, "/"));
            Assert.Throws<ArgumentException>(() => new WebSocketTransportFactory().CreateConnector("ws://", 1));
            Assert.Throws<ArgumentException>(() => new WebSocketSettings { Path = "fomoxa" });
            Assert.Throws<ArgumentException>(() => new WebSocketSettings { Path = "/a?b" });
        }

        private static ConnectStatus ConnectToScriptedServer(Func<string, string> response)
        {
            using var owned = new StartedListener();
            TcpListener tcp = owned.Listener;
            ushort port = (ushort)((IPEndPoint)tcp.LocalEndpoint).Port;
            using ITransportConnector connector = new WebSocketTransportFactory().CreateConnector("127.0.0.1", port);
            Task<TcpClient> accepting = tcp.AcceptTcpClientAsync();
            PollConnector(connector, () => !accepting.IsCompleted, out _);
            using TcpClient accepted = accepting.Result;
            NetworkStream stream = accepted.GetStream();
            string request = ReadHeader(stream, connector);
            string key = request.Split(new[] { "\r\n" }, StringSplitOptions.None).First(line => line.StartsWith("Sec-WebSocket-Key: ")).Substring(19);
            byte[] bytes = Encoding.ASCII.GetBytes(response(key));
            stream.Write(bytes, 0, bytes.Length);
            ConnectStatus status = PollConnector(connector, () => true, out ITransport transport);
            transport?.Dispose();
            return status;
        }

        private static string Rejected(IListenerTransport listener, ushort port, string request)
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(IPAddress.Loopback, port);
            socket.Send(Encoding.ASCII.GetBytes(request));
            var response = new byte[256];
            int used = 0;
            socket.Blocking = false;
            Wait(() =>
            {
                Assert.AreNotEqual(AcceptStatus.Accepted, listener.Accept().Status);
                int read = socket.Receive(response, used, response.Length - used, SocketFlags.None, out SocketError error);
                if ((error == SocketError.Success && read == 0) || (error != SocketError.Success && error != SocketError.WouldBlock))
                {
                    return true;
                }

                used += Math.Max(read, 0);
                return false;
            });
            return Encoding.ASCII.GetString(response, 0, used).Split(' ')[1];
        }

        private static (ITransport Client, ITransport Server) Connect(ITransportConnector connector, IListenerTransport listener)
        {
            ITransport server = null;
            ITransport client = null;
            Wait(() =>
            {
                AcceptOutcome accepted = listener.Accept();
                if (accepted.Status == AcceptStatus.Accepted)
                {
                    server = accepted.Transport;
                }

                if (client == null)
                {
                    ConnectStatus status = connector.Poll(out client);
                    Assert.AreNotEqual(ConnectStatus.Failed, status);
                }

                return client != null && server != null;
            });
            return (client, server);
        }

        private static ITransport AcceptWhile(IListenerTransport listener, Func<bool> waiting)
        {
            ITransport server = null;
            Wait(() =>
            {
                AcceptOutcome accepted = listener.Accept();
                if (accepted.Status == AcceptStatus.Accepted)
                {
                    server = accepted.Transport;
                }

                return server != null && !waiting();
            });
            return server;
        }

        private static ConnectStatus PollConnector(ITransportConnector connector, Func<bool> waiting, out ITransport transport)
        {
            ITransport connected = null;
            ConnectStatus status = ConnectStatus.Pending;
            Wait(() =>
            {
                status = connector.Poll(out connected);
                return status != ConnectStatus.Pending || !waiting();
            });
            transport = connected;
            return status;
        }

        private static string ReadHeader(NetworkStream stream, ITransportConnector connector)
        {
            var header = new StringBuilder();
            var one = new byte[1];
            stream.ReadTimeout = 50;
            var clock = Stopwatch.StartNew();
            while (!header.ToString().EndsWith("\r\n\r\n") && clock.Elapsed < Limit)
            {
                connector.Poll(out _);
                try
                {
                    if (stream.Read(one, 0, 1) == 1)
                    {
                        header.Append((char)one[0]);
                    }
                }
                catch (System.IO.IOException)
                {
                }
            }

            stream.ReadTimeout = Timeout.Infinite;
            return header.ToString();
        }

        private static SendOutcome SendAll(ITransport transport, byte[] message)
        {
            SendOutcome outcome = default;
            Wait(() =>
            {
                outcome = transport.Send(message);
                return outcome.Signal != TransportSignal.WouldBlock;
            });
            return outcome;
        }

        private static byte[] ReceiveOne(ITransport receiver, ITransport sender)
        {
            var buffer = new byte[256 * 1024];
            ReceiveOutcome outcome = Poll(() => receiver.Receive(buffer), sender);
            Assert.AreEqual(TransportSignal.Ok, outcome.Signal);
            return buffer.Take(outcome.Count).ToArray();
        }

        private static ReceiveOutcome Poll(Func<ReceiveOutcome> receive, ITransport other, Func<TransportSignal, bool> done = null)
        {
            ReceiveOutcome outcome = default;
            Wait(() =>
            {
                other?.Receive(Span<byte>.Empty);
                outcome = receive();
                return done?.Invoke(outcome.Signal) ?? outcome.Signal != TransportSignal.WouldBlock;
            });
            return outcome;
        }

        private static void Wait(Func<bool> done)
        {
            var clock = Stopwatch.StartNew();
            while (!done())
            {
                Assert.Less(clock.Elapsed, Limit, "timed out");
                Thread.Sleep(1);
            }
        }

        private static byte[] Pattern(int length)
        {
            var bytes = new byte[length];
            for (int index = 0; index < length; index++)
            {
                bytes[index] = (byte)(index * 31 + 7);
            }

            return bytes;
        }

        private sealed class StartedListener : IDisposable
        {
            public StartedListener()
            {
                Listener = new TcpListener(IPAddress.Loopback, 0);
                Listener.Start();
            }

            public TcpListener Listener { get; }

            public void Dispose() => Listener.Stop();
        }

        private sealed class RawClient : IDisposable
        {
            private readonly Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            private readonly byte[] header = new byte[1024];
            private int used;
            private bool upgraded;

            public RawClient(ushort port, string path, string origin = null)
            {
                socket.Connect(IPAddress.Loopback, port);
                string originLine = origin == null ? "" : $"Origin: {origin}\r\n";
                socket.Send(Encoding.ASCII.GetBytes($"GET {path} HTTP/1.1\r\nHost: localhost\r\n{originLine}Upgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n\r\n"));
                socket.Blocking = false;
            }

            public bool Available => socket.Available > 0;

            public bool Upgraded(IListenerTransport listener)
            {
                if (upgraded)
                {
                    return true;
                }

                int read = socket.Receive(header, used, header.Length - used, SocketFlags.None, out SocketError error);
                used += error == SocketError.Success ? read : 0;
                string text = Encoding.ASCII.GetString(header, 0, used);
                if (!text.EndsWith("\r\n\r\n"))
                {
                    return false;
                }

                StringAssert.StartsWith("HTTP/1.1 101 Switching Protocols\r\n", text);
                StringAssert.Contains("Sec-WebSocket-Accept: s3pPLMBiTxaQ9kYGzzhZRbK+xOo=\r\n", text);
                upgraded = true;
                return true;
            }

            public void Send(byte opcode, bool masked, byte[] payload)
            {
                var frame = new byte[2 + (masked ? 4 : 0) + payload.Length];
                frame[0] = (byte)(0x80 | opcode);
                frame[1] = (byte)((masked ? 0x80 : 0) | payload.Length);
                byte[] mask = { 1, 2, 3, 4 };
                int at = 2;
                if (masked)
                {
                    Array.Copy(mask, 0, frame, 2, 4);
                    at = 6;
                }

                for (int index = 0; index < payload.Length; index++)
                {
                    frame[at + index] = masked ? (byte)(payload[index] ^ mask[index & 3]) : payload[index];
                }

                socket.Blocking = true;
                socket.Send(frame);
                socket.Blocking = false;
            }

            public (byte Opcode, byte[] Payload) ReadFrame()
            {
                socket.Blocking = true;
                var head = new byte[2];
                socket.Receive(head);
                var payload = new byte[head[1] & 0x7F];
                if (payload.Length > 0)
                {
                    socket.Receive(payload);
                }

                socket.Blocking = false;
                return ((byte)(head[0] & 0x0F), payload);
            }

            public void Dispose() => socket.Dispose();
        }
    }
}
