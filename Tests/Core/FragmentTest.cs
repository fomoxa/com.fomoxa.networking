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
    public sealed class FragmentTest
    {
        private const uint OrderId = 0x2000_0003;
        private const uint PositionId = 0x2000_0004;
        private const int UdpBudget = 1200;
        private static readonly TimeSpan Now = TimeSpan.FromSeconds(10);

        private static Schema HostSchema() =>
            new Schema(0xCAFE, new[]
            {
                new MessageSchema(OrderId, 0xF00D, new ulong[] { 0xF00D }),
                new MessageSchema(PositionId, 0xF00E, new ulong[] { 0xF00E }),
            });

        private static MessageChannels Channels()
        {
            var channels = new MessageChannels();
            channels.Set(OrderId, Channel.ReliableOrdered);
            return channels;
        }

        private static SessionOutbox Outbox(int window = 1024, int frameBudget = UdpBudget) =>
            new SessionOutbox(new OutgoingQueue(64, 4 * Fragments.MaxLength), TestBundles.Protocol(Channels(), frameBudget), new ReliableChannel(window));

        private static byte[] Pattern(int length)
        {
            var data = new byte[length];
            for (int index = 0; index < length; index++)
            {
                data[index] = (byte)(index * 7 + 1);
            }

            return data;
        }

        private static byte[] Start(uint messageId, uint length, byte[] data) =>
            FragmentStartNetAdapter.Instance.Encode(new FragmentStart { MessageId = messageId, Length = length, Data = data }).ToArray();

        private static byte[] Part(byte[] data) =>
            FragmentPartNetAdapter.Instance.Encode(new FragmentPart { Data = data }).ToArray();

        private static List<BundleEntry> Entries(uint frameMessageId, ReadOnlySpan<byte> frame)
        {
            Assert.AreEqual(TestBundles.MessageId, frameMessageId);
            var bundle = new MessageBundle();
            MessageBundleNetAdapter.Instance.Decode(frame.ToArray(), ref bundle);
            return bundle.Entries.ConvertAll(entry => new BundleEntry { MessageId = entry.MessageId, Data = entry.Data.ToArray() });
        }

        private static List<BundleEntry> FlushWithinBudget(SessionOutbox outbox, List<SendDroppedArgs> dropped = null)
        {
            var sent = new List<BundleEntry>();
            outbox.Flush((messageId, payload) =>
            {
                Assert.LessOrEqual(FomoxaWire.DataFrameHeaderSize + payload.Length, UdpBudget);
                sent.AddRange(Entries(messageId, payload));
                return SendStatus.Sent;
            }, Now, 7, dropped == null ? null : dropped.Add);
            return sent;
        }

        private static ushort SeqOf(BundleEntry entry) => SeqHeader.Read(entry.Data.Span);

        private static byte[] Reassemble(List<BundleEntry> fragments, out uint messageId)
        {
            Assert.AreEqual(TestBundles.FragmentStartId, fragments[0].MessageId);
            var start = new FragmentStart();
            FragmentStartNetAdapter.Instance.Decode(fragments[0].Data.Slice(SeqHeader.Length), ref start);
            var whole = new List<byte>(start.Data.ToArray());
            var part = new FragmentPart();
            for (int index = 1; index < fragments.Count; index++)
            {
                Assert.AreEqual(TestBundles.FragmentPartId, fragments[index].MessageId);
                FragmentPartNetAdapter.Instance.Decode(fragments[index].Data.Slice(SeqHeader.Length), ref part);
                whole.AddRange(part.Data.ToArray());
            }

            Assert.AreEqual(start.Length, (uint)whole.Count);
            messageId = start.MessageId;
            return whole.ToArray();
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
            public readonly List<byte[]> ServerOrders = new List<byte[]>();
            public readonly List<ConnectionStateArgs> PeerStops = new List<ConnectionStateArgs>();
            public readonly ServerSession Server;
            public readonly ClientSession Client;
            public TimeSpan Time = TimeSpan.Zero;

            public Host(Func<int, bool> dropClientDataFrame)
            {
                var serverDispatcher = new MessageDispatcher(HostSchema());
                serverDispatcher.Register(OrderId, (peerId, payload) => ServerOrders.Add(payload.ToArray()));
                Server = new ServerSession(HostSchema(), new SessionConfig(), new SessionLimits(), serverDispatcher, TestBundles.Protocol(Channels(), UdpBudget)).SendingLocalPeer();
                Client = new ClientSession(HostSchema(), new SessionConfig(), new SessionLimits(), new MessageDispatcher(HostSchema()), TestBundles.Protocol(Channels(), UdpBudget)).AcceptingLocalPeer();
                Server.OnRemoteConnectionState += args =>
                {
                    if (args.State == ConnectionState.Stopped)
                    {
                        PeerStops.Add(args);
                    }
                };
                Server.Start(Listener);
                Client.Start(new DroppingTransport(Listener.Connect(), dropClientDataFrame), Time);
                Step(20);
            }

            public void Step(int count)
            {
                for (int step = 0; step < count; step++)
                {
                    Time += TimeSpan.FromMilliseconds(16);
                    Server.Tick(Time);
                    Client.Tick(Time);
                    Server.Flush();
                    Client.Flush();
                }
            }
        }

        private static StopReason PeerStopAfter(params (uint MessageId, byte[] Payload)[] sends)
        {
            var host = new Host(frame => false);
            foreach ((uint messageId, byte[] payload) in sends)
            {
                Assert.AreEqual(SendResult.Queued, host.Client.Send(messageId, payload));
            }

            host.Step(10);
            Assert.AreEqual(1, host.PeerStops.Count);
            return host.PeerStops[0].Reason;
        }

        [Test]
        public void AReliableMessageLargerThanTheBudgetIsSplitIntoFragmentsOnConsecutiveSeqs()
        {
            SessionOutbox outbox = Outbox();
            byte[] order = Pattern(5000);
            Assert.AreEqual(EnqueueResult.Queued, outbox.Enqueue(OrderId, order));

            List<BundleEntry> sent = FlushWithinBudget(outbox);

            Assert.AreEqual(5, sent.Count);
            for (int index = 0; index < sent.Count; index++)
            {
                Assert.AreEqual((ushort)index, SeqOf(sent[index]));
            }

            Assert.AreEqual(order, Reassemble(sent, out uint messageId));
            Assert.AreEqual(OrderId, messageId);
            Assert.AreEqual(0, outbox.Queue.Count);
            Assert.AreEqual(5, outbox.Reliable.InFlightCount);
        }

        [Test]
        public void AnUnreliableMessageLargerThanTheBudgetGoesWholeInABundleOfOne()
        {
            SessionOutbox outbox = Outbox();
            byte[] position = Pattern(5000);
            outbox.Enqueue(PositionId, position);
            var sent = new List<BundleEntry>();

            outbox.Flush((messageId, payload) =>
            {
                sent.AddRange(Entries(messageId, payload));
                return SendStatus.Sent;
            }, Now, 7, null);

            Assert.AreEqual(1, sent.Count);
            Assert.AreEqual(PositionId, sent[0].MessageId);
            Assert.AreEqual(position, sent[0].Data.ToArray());
        }

        [Test]
        public void AFullWindowKeepsTheRestOfTheMessageAtTheHeadUntilAcked()
        {
            SessionOutbox outbox = Outbox(window: 2);
            outbox.Enqueue(OrderId, Pattern(5000));
            outbox.Enqueue(OrderId, new byte[] { 9 });
            outbox.Enqueue(PositionId, new byte[] { 8 });
            var sent = new List<BundleEntry>();

            sent.AddRange(FlushWithinBudget(outbox));
            Assert.AreEqual(2, sent.Count);
            Assert.AreEqual(3, outbox.Queue.Count);
            for (int round = 0; round < 2; round++)
            {
                outbox.Reliable.OnAck(outbox.Reliable.NextSendSeq, 0, Now);
                sent.AddRange(FlushWithinBudget(outbox));
            }

            uint[] ids = sent.ConvertAll(entry => entry.MessageId).ToArray();
            uint start = TestBundles.FragmentStartId;
            uint part = TestBundles.FragmentPartId;
            Assert.AreEqual(new[] { start, part, part, part, part, OrderId, PositionId }, ids);
            for (int index = 0; index < 6; index++)
            {
                Assert.AreEqual((ushort)index, SeqOf(sent[index]));
            }

            Assert.AreEqual(0, outbox.Queue.Count);
        }

        [Test]
        public void AReliableMessageTooLargeToReassembleIsRejectedWhenQueued()
        {
            SessionOutbox outbox = Outbox();

            Assert.AreEqual(EnqueueResult.TooLargeForReassembly, outbox.Enqueue(OrderId, new byte[Fragments.MaxLength + 1]));
            Assert.AreEqual(EnqueueResult.TooLargeForReassembly, outbox.EnqueueToObject(OrderId, 1, 0, new byte[Fragments.MaxLength]));
            Assert.AreEqual(EnqueueResult.Queued, outbox.Enqueue(OrderId, new byte[Fragments.MaxLength]));
            Assert.AreEqual(EnqueueResult.Queued, outbox.Enqueue(PositionId, new byte[Fragments.MaxLength + 1]));
            Assert.AreEqual(EnqueueResult.Queued, Outbox(frameBudget: FomoxaWire.MaxDataFrameSize).Enqueue(OrderId, new byte[Fragments.MaxLength + 1]));
            Assert.AreEqual(SendResult.TooLargeForReassembly, SendResults.From(EnqueueResult.TooLargeForReassembly));
        }

        [Test]
        public void AnObjectLayerMessageTooLargeToReassembleIsDroppedAndTheFlushGoesOn()
        {
            SessionOutbox outbox = Outbox();
            outbox.Enqueue(OrderId, new byte[] { 1 });
            Assert.AreEqual(EnqueueResult.Queued, outbox.EnqueueUnbounded(OrderId, new byte[Fragments.MaxLength + 1]));
            outbox.Enqueue(OrderId, new byte[] { 2 });
            var dropped = new List<SendDroppedArgs>();

            List<BundleEntry> sent = FlushWithinBudget(outbox, dropped);

            Assert.AreEqual(1, dropped.Count);
            Assert.AreEqual(OrderId, dropped[0].MessageId);
            Assert.AreEqual(SendStatus.TooLarge, dropped[0].Reason);
            Assert.AreEqual(2, sent.Count);
            Assert.AreEqual(new byte[] { 0, 0, 1 }, sent[0].Data.ToArray());
            Assert.AreEqual(new byte[] { 1, 0, 2 }, sent[1].Data.ToArray());
            Assert.AreEqual(0, outbox.Queue.Count);
        }

        [Test]
        public void ALargeReliableMessageArrivesWholeAndInOrderDespiteLostFragments()
        {
            var host = new Host(frame => frame == 1 || frame == 3);
            byte[] order = Pattern(20000);

            Assert.AreEqual(SendResult.Queued, host.Client.Send(OrderId, order));
            Assert.AreEqual(SendResult.Queued, host.Client.Send(OrderId, new byte[] { 9 }));
            host.Step(120);

            Assert.AreEqual(2, host.ServerOrders.Count);
            Assert.AreEqual(order, host.ServerOrders[0]);
            Assert.AreEqual(new byte[] { 9 }, host.ServerOrders[1]);
            Assert.AreEqual(0, host.PeerStops.Count);
        }

        [Test]
        public void APartWithoutAStartEndsThePeerWithInvalidFragment()
        {
            Assert.AreEqual(StopReason.InvalidFragment, PeerStopAfter((TestBundles.FragmentPartId, Part(new byte[] { 1 }))));
        }

        [Test]
        public void AStartWhileAssemblingEndsThePeerWithInvalidFragment()
        {
            byte[] start = Start(OrderId, 10, new byte[] { 1 });
            Assert.AreEqual(StopReason.InvalidFragment, PeerStopAfter((TestBundles.FragmentStartId, start), (TestBundles.FragmentStartId, start)));
        }

        [Test]
        public void ALengthAboveTheLimitEndsThePeerWithInvalidFragment()
        {
            byte[] start = Start(OrderId, Fragments.MaxLength + 1, new byte[] { 1 });
            Assert.AreEqual(StopReason.InvalidFragment, PeerStopAfter((TestBundles.FragmentStartId, start)));
        }

        [Test]
        public void APartPastTheLengthEndsThePeerWithInvalidFragment()
        {
            Assert.AreEqual(StopReason.InvalidFragment, PeerStopAfter(
                (TestBundles.FragmentStartId, Start(OrderId, 2, new byte[] { 1 })),
                (TestBundles.FragmentPartId, Part(new byte[] { 2, 3 }))));
        }

        [Test]
        public void AStartThatCompletesTheMessageAtOnceEndsThePeerWithInvalidFragment()
        {
            Assert.AreEqual(StopReason.InvalidFragment, PeerStopAfter((TestBundles.FragmentStartId, Start(OrderId, 1, new byte[] { 1 }))));
            Assert.AreEqual(StopReason.InvalidFragment, PeerStopAfter((TestBundles.FragmentStartId, Start(OrderId, 0, Array.Empty<byte>()))));
        }

        [Test]
        public void AnAssembledMessageOffTheReliableChannelEndsThePeerWithInvalidFragment()
        {
            Assert.AreEqual(StopReason.InvalidFragment, PeerStopAfter(
                (TestBundles.FragmentStartId, Start(PositionId, 2, new byte[] { 1 })),
                (TestBundles.FragmentPartId, Part(new byte[] { 2 }))));
        }
    }
}
