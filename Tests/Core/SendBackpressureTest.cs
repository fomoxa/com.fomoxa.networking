using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class SendBackpressureTest
    {
        private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

        [Test]
        public void ATcpClientSendTheSocketCannotTakeIsWouldBlock()
        {
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(4);
            using ITransportConnector connector = new TcpTransportFactory().CreateConnector("127.0.0.1", (ushort)((IPEndPoint)listener.LocalEndPoint).Port);
            ITransport client = null;
            Wait(() => connector.Poll(out client) == ConnectStatus.Connected);
            Assert.IsTrue(listener.Poll(5_000_000, SelectMode.SelectRead));
            using Socket peer = listener.Accept();
            using (client)
            {
                long accepted = Backpressure.FillUntilWouldBlock(client, new byte[64 * 1024]);

                Assert.AreEqual(accepted, Backpressure.Drain(peer, client, accepted));
            }
        }

        [Test]
        public void ATcpServerSendTheSocketCannotTakeIsWouldBlock()
        {
            using IListenerTransport listener = new TcpTransportFactory().CreateListener(0, out ushort port);
            using var peer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            peer.Connect(new IPEndPoint(IPAddress.Loopback, port));
            ITransport server = null;
            Wait(() =>
            {
                AcceptOutcome outcome = listener.Accept();
                server = outcome.Status == AcceptStatus.Accepted ? outcome.Transport : null;
                return server != null;
            });
            using (server)
            {
                long accepted = Backpressure.FillUntilWouldBlock(server, new byte[64 * 1024]);

                Assert.AreEqual(accepted, Backpressure.Drain(peer, server, accepted));
            }
        }

        [Test]
        public void AWebSocketSendTheSocketCannotTakeIsWouldBlockOnBothEnds()
        {
            var factory = new WebSocketTransportFactory();
            using IListenerTransport listener = factory.CreateListener(0, out ushort port);
            using ITransportConnector connector = factory.CreateConnector("127.0.0.1", port);
            ITransport server = null;
            ITransport client = null;
            Wait(() =>
            {
                AcceptOutcome outcome = listener.Accept();
                if (outcome.Status == AcceptStatus.Accepted)
                {
                    server = outcome.Transport;
                }

                if (client == null)
                {
                    connector.Poll(out client);
                }

                return client != null && server != null;
            });
            using (client)
            using (server)
            {
                long fromClient = Backpressure.FillUntilWouldBlock(client, new byte[16 * 1024]);
                Assert.AreEqual(fromClient, Backpressure.Drain(server, client, fromClient));

                long fromServer = Backpressure.FillUntilWouldBlock(server, new byte[16 * 1024]);
                Assert.AreEqual(fromServer, Backpressure.Drain(client, server, fromServer));
            }
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
    }

    internal static class Backpressure
    {
        private const long MostBytes = 256L * 1024 * 1024;
        private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

        public static long FillUntilWouldBlock(ITransport transport, byte[] chunk)
        {
            long accepted = 0;
            TransportSignal last = TransportSignal.Ok;
            var filling = new Thread(() =>
            {
                while (last == TransportSignal.Ok && accepted < MostBytes)
                {
                    last = transport.Send(chunk).Signal;
                    if (last == TransportSignal.Ok)
                    {
                        accepted += chunk.Length;
                    }
                }
            }) { IsBackground = true };
            filling.Start();

            Assert.IsTrue(filling.Join(Limit), "a send blocked the calling thread");
            Assert.AreEqual(TransportSignal.WouldBlock, last);
            return accepted;
        }

        public static long Drain(Socket receiver, ITransport sender, long expected)
        {
            long read = 0;
            var buffer = new byte[64 * 1024];
            var scratch = new byte[16];
            var clock = Stopwatch.StartNew();
            while (read < expected && clock.Elapsed < Limit)
            {
                sender.Receive(scratch);
                if (receiver.Poll(1000, SelectMode.SelectRead))
                {
                    read += receiver.Receive(buffer);
                }
            }

            return read;
        }

        public static long Drain(ITransport receiver, ITransport sender, long expected)
        {
            long read = 0;
            var buffer = new byte[256 * 1024];
            var scratch = new byte[16];
            var clock = Stopwatch.StartNew();
            while (read < expected && clock.Elapsed < Limit)
            {
                sender.Receive(scratch);
                ReceiveOutcome outcome = receiver.Receive(buffer);
                if (outcome.Signal == TransportSignal.Ok)
                {
                    read += outcome.Count;
                }
                else
                {
                    Assert.AreEqual(TransportSignal.WouldBlock, outcome.Signal);
                    Thread.Sleep(1);
                }
            }

            return read;
        }
    }
}
