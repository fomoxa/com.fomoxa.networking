using System;
using System.Collections.Generic;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class ReliableSessionTest
    {
        private const uint OrderId = 0x2000_0003;
        private const uint PositionId = 0x2000_0004;

        private static Schema HostSchema() =>
            new Schema(0xCAFE, new[]
            {
                new MessageSchema(OrderId, 0xF00D, new ulong[] { 0xF00D }),
                new MessageSchema(PositionId, 0xF00E, new ulong[] { 0xF00E }),
            });

        private static SessionProtocol Protocol()
        {
            var channels = new MessageChannels();
            channels.Set(OrderId, Channel.ReliableOrdered);
            return TestBundles.Protocol(channels);
        }

        private sealed class DroppingTransport : ITransport
        {
            private readonly ITransport inner;
            private readonly Func<int, bool> dropDataFrame;
            private int dataFrames;

            public DroppingTransport(ITransport inner, Func<int, bool> dropDataFrame)
            {
                this.inner = inner;
                this.dropDataFrame = dropDataFrame;
            }

            public TransportKind Kind => inner.Kind;

            public SendOutcome Send(ReadOnlySpan<byte> bytes)
            {
                if (bytes[0] == (byte)FrameType.Data && dropDataFrame(dataFrames++))
                {
                    return SendOutcome.Ok;
                }

                return inner.Send(bytes);
            }

            public ReceiveOutcome Receive(Span<byte> buffer) => inner.Receive(buffer);

            public void CloseGracefully() => inner.CloseGracefully();

            public void Dispose() => inner.Dispose();
        }

        private sealed class Host
        {
            public readonly LoopbackListener Listener = new LoopbackListener(256);
            public readonly List<byte> ServerOrders = new List<byte>();
            public readonly List<byte> ServerPositions = new List<byte>();
            public readonly List<byte> ClientOrders = new List<byte>();
            public readonly ServerSession Server;
            public readonly ClientSession Client;
            public ulong PeerId;
            public TimeSpan Now = TimeSpan.Zero;

            public Host(Func<int, bool> dropClientDataFrame)
            {
                var serverDispatcher = new MessageDispatcher(HostSchema());
                var clientDispatcher = new MessageDispatcher(HostSchema());
                serverDispatcher.Register(OrderId, (peerId, payload) => ServerOrders.Add(payload.Span[0]));
                serverDispatcher.Register(PositionId, (peerId, payload) => ServerPositions.Add(payload.Span[0]));
                clientDispatcher.Register(OrderId, (peerId, payload) => ClientOrders.Add(payload.Span[0]));
                Server = new ServerSession(HostSchema(), new SessionConfig(), new SessionLimits(), serverDispatcher, Protocol());
                Client = new ClientSession(HostSchema(), new SessionConfig(), new SessionLimits(), clientDispatcher, Protocol());
                Server.OnRemoteConnectionState += args => PeerId = args.PeerId;
                Server.Start(Listener);
                Client.Start(new DroppingTransport(Listener.Connect(), dropClientDataFrame), Now);
                Step(20);
            }

            public void Step(int count)
            {
                for (int step = 0; step < count; step++)
                {
                    Now += TimeSpan.FromMilliseconds(16);
                    Server.Tick(Now);
                    Client.Tick(Now);
                    Server.Flush();
                    Client.Flush();
                }
            }
        }

        [Test]
        public void ReliableMessagesArriveOnceAndInOrderDespiteLostFrames()
        {
            var host = new Host(frame => frame == 0 || frame == 2);

            for (byte value = 1; value <= 5; value++)
            {
                Assert.AreEqual(SendResult.Queued, host.Client.Send(OrderId, new[] { value }));
                Assert.AreEqual(SendResult.Queued, host.Client.Send(PositionId, new[] { value }));
                host.Step(1);
            }

            host.Step(60);

            Assert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, host.ServerOrders.ToArray());
            Assert.AreEqual(new byte[] { 2, 4, 5 }, host.ServerPositions.ToArray());
        }

        [Test]
        public void LostAcksCauseResendsThatAreDeliveredOnlyOnce()
        {
            var host = new Host(frame => frame < 4);

            Assert.AreEqual(SendResult.Queued, host.Server.Send(host.PeerId, OrderId, new byte[] { 7 }));
            Assert.AreEqual(SendResult.Queued, host.Server.Send(host.PeerId, OrderId, new byte[] { 8 }));
            host.Step(120);

            Assert.AreEqual(new byte[] { 7, 8 }, host.ClientOrders.ToArray());
        }

        [Test]
        public void ReliableFrameFromTooFarAheadIsDroppedAsMalformed()
        {
            var listener = new LoopbackListener(64);
            var dropped = new List<ReceiveDroppedArgs>();
            var server = new ServerSession(HostSchema(), new SessionConfig(), new SessionLimits(), new MessageDispatcher(HostSchema()), Protocol());
            server.OnReceiveDropped += dropped.Add;
            server.Start(listener);
            using var client = FomoxaConnection.Connect(listener.Connect(), HostSchema(), new SessionConfig(), TimeSpan.Zero);
            TimeSpan now = TimeSpan.Zero;
            for (int step = 0; step < 20 && !client.IsReady; step++)
            {
                now += TimeSpan.FromMilliseconds(16);
                server.Tick(now);
                client.Tick(now);
            }

            var data = new byte[SeqHeader.Length + 1];
            SeqHeader.Write(data, ReliableChannel.MaxWindow);
            var bundle = new MessageBundle();
            bundle.Entries.Add(new BundleEntry { MessageId = OrderId, Data = data });
            client.Send(TestBundles.MessageId, MessageBundleNetAdapter.Instance.Encode(bundle).Span);
            server.Tick(now + TimeSpan.FromMilliseconds(16));

            Assert.AreEqual(1, dropped.Count);
            server.Stop();
        }
    }
}
