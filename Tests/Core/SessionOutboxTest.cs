using System;
using System.Collections.Generic;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class SessionOutboxTest
    {
        private const uint OrderId = 40;
        private static readonly TimeSpan Now = TimeSpan.FromSeconds(10);

        private static SessionOutbox OutboxOfThree(int frameBudget = FomoxaWire.MaxDataFrameSize)
        {
            SessionOutbox outbox = Outbox(frameBudget: frameBudget);
            outbox.Enqueue(10, new byte[] { 1, 2, 3 });
            outbox.Enqueue(20, new byte[] { 4 });
            outbox.Enqueue(30, new byte[] { 5 });
            return outbox;
        }

        private static SessionOutbox Outbox(int window = 1024, int frameBudget = FomoxaWire.MaxDataFrameSize)
        {
            var channels = new MessageChannels();
            channels.Set(OrderId, Channel.ReliableOrdered);
            return new SessionOutbox(new OutgoingQueue(64, 1024), TestBundles.Protocol(channels, frameBudget), new ReliableChannel(window));
        }

        private static List<BundleEntry> Entries(uint frameMessageId, ReadOnlySpan<byte> frame)
        {
            Assert.AreEqual(TestBundles.MessageId, frameMessageId);
            var bundle = new MessageBundle();
            MessageBundleNetAdapter.Instance.Decode(frame.ToArray(), ref bundle);
            return bundle.Entries.ConvertAll(entry => new BundleEntry { MessageId = entry.MessageId, Data = entry.Data.ToArray() });
        }

        private static uint[] EntryIds(uint frameMessageId, ReadOnlySpan<byte> frame) =>
            Entries(frameMessageId, frame).ConvertAll(entry => entry.MessageId).ToArray();

        private static ushort SeqOf(BundleEntry entry) => SeqHeader.Read(entry.Data.Span);

        [Test]
        public void OneFlushSendsEveryQueuedMessageInOneBundleInEnqueueOrder()
        {
            SessionOutbox outbox = OutboxOfThree();
            var frames = new List<List<BundleEntry>>();

            int discarded = outbox.Flush((messageId, payload) =>
            {
                frames.Add(Entries(messageId, payload));
                return SendStatus.Sent;
            }, Now, 7, null);

            Assert.AreEqual(0, discarded);
            Assert.AreEqual(1, frames.Count);
            Assert.AreEqual(new uint[] { 10, 20, 30 }, frames[0].ConvertAll(entry => entry.MessageId).ToArray());
            Assert.AreEqual(new byte[] { 1, 2, 3 }, frames[0][0].Data.ToArray());
            Assert.AreEqual(0, outbox.Queue.Count);
            Assert.AreEqual(0, outbox.Reliable.InFlightCount);
        }

        [Test]
        public void FrameBudgetSplitsTheQueueIntoSeveralBundles()
        {
            int frameBudget = FomoxaWire.DataFrameHeaderSize + 4 + (8 + 3) + (8 + 1);
            SessionOutbox outbox = OutboxOfThree(frameBudget);
            var frames = new List<uint[]>();

            outbox.Flush((messageId, payload) =>
            {
                Assert.LessOrEqual(FomoxaWire.DataFrameHeaderSize + payload.Length, frameBudget);
                frames.Add(EntryIds(messageId, payload));
                return SendStatus.Sent;
            }, Now, 7, null);

            Assert.AreEqual(2, frames.Count);
            Assert.AreEqual(new uint[] { 10, 20 }, frames[0]);
            Assert.AreEqual(new uint[] { 30 }, frames[1]);
        }

        [Test]
        public void MessageLargerThanTheBudgetGoesAloneInItsOwnBundle()
        {
            SessionOutbox outbox = OutboxOfThree(FomoxaWire.DataFrameHeaderSize + 4 + (8 + 1));
            var frames = new List<uint[]>();

            outbox.Flush((messageId, payload) =>
            {
                frames.Add(EntryIds(messageId, payload));
                return SendStatus.Sent;
            }, Now, 7, null);

            Assert.AreEqual(3, frames.Count);
            Assert.AreEqual(new uint[] { 10 }, frames[0]);
            Assert.AreEqual(new uint[] { 20 }, frames[1]);
            Assert.AreEqual(new uint[] { 30 }, frames[2]);
        }

        [Test]
        public void TooLargeSplitsTheBundleAndResendsInTheSameFlush()
        {
            SessionOutbox outbox = OutboxOfThree();
            var attempts = new List<uint[]>();

            int discarded = outbox.Flush((messageId, payload) =>
            {
                uint[] ids = EntryIds(messageId, payload);
                attempts.Add(ids);
                return ids.Length == 3 ? SendStatus.TooLarge : SendStatus.Sent;
            }, Now, 7, null);

            Assert.AreEqual(0, discarded);
            Assert.AreEqual(3, attempts.Count);
            Assert.AreEqual(new uint[] { 10, 20, 30 }, attempts[0]);
            Assert.AreEqual(new uint[] { 10 }, attempts[1]);
            Assert.AreEqual(new uint[] { 20, 30 }, attempts[2]);
            Assert.AreEqual(0, outbox.Queue.Count);
        }

        [Test]
        public void TooLargeForABundleOfOneDropsThatMessageReportsItAndFlushesTheRest()
        {
            SessionOutbox outbox = OutboxOfThree();
            var sent = new List<uint[]>();
            var dropped = new List<SendDroppedArgs>();

            int discarded = outbox.Flush((messageId, payload) =>
            {
                uint[] ids = EntryIds(messageId, payload);
                if (Array.IndexOf(ids, 10u) >= 0)
                {
                    return SendStatus.TooLarge;
                }

                sent.Add(ids);
                return SendStatus.Sent;
            }, Now, 7, dropped.Add);

            Assert.AreEqual(0, discarded);
            Assert.AreEqual(1, sent.Count);
            Assert.AreEqual(new uint[] { 20, 30 }, sent[0]);
            Assert.AreEqual(1, dropped.Count);
            Assert.AreEqual(7ul, dropped[0].PeerId);
            Assert.AreEqual(10u, dropped[0].MessageId);
            Assert.AreEqual(3, dropped[0].PayloadLength);
            Assert.AreEqual(SendStatus.TooLarge, dropped[0].Reason);
            Assert.AreEqual(0, outbox.Queue.Count);
        }

        [Test]
        public void CongestedAndNotReadyStopTheFlushWithEveryMessageOfTheBundleQueued()
        {
            foreach (SendStatus refusal in new[] { SendStatus.Congested, SendStatus.NotReady })
            {
                SessionOutbox outbox = OutboxOfThree();
                var dropped = new List<SendDroppedArgs>();

                int discarded = outbox.Flush((messageId, payload) => refusal, Now, 1, dropped.Add);

                Assert.AreEqual(0, discarded);
                Assert.AreEqual(3, outbox.Queue.Count);
                Assert.AreEqual(0, dropped.Count);
            }
        }

        [Test]
        public void ClosedClearsTheQueueAndReturnsHowManyWereDiscarded()
        {
            SessionOutbox outbox = OutboxOfThree();

            int discarded = outbox.Flush((messageId, payload) => SendStatus.Closed, Now, 1, null);

            Assert.AreEqual(3, discarded);
            Assert.AreEqual(0, outbox.Queue.Count);
            Assert.AreEqual(0, outbox.Queue.PendingBytes);
        }

        [Test]
        public void EmptyQueueSendsNothing()
        {
            SessionOutbox outbox = Outbox();
            int attempts = 0;

            outbox.Flush((messageId, payload) =>
            {
                attempts++;
                return SendStatus.Sent;
            }, Now, 1, null);

            Assert.AreEqual(0, attempts);
        }

        [Test]
        public void ReliableMessagesCarryConsecutiveSeqAndStayInFlightUntilAcked()
        {
            SessionOutbox outbox = Outbox();
            outbox.Enqueue(OrderId, new byte[] { 1 });
            outbox.Enqueue(10, new byte[] { 2 });
            outbox.EnqueueToObject(OrderId, 7, 1, new byte[] { 3 });
            var frames = new List<List<BundleEntry>>();

            outbox.Flush((messageId, payload) =>
            {
                frames.Add(Entries(messageId, payload));
                return SendStatus.Sent;
            }, Now, 1, null);

            List<BundleEntry> entries = frames[0];
            Assert.AreEqual(new byte[] { 0, 0, 1 }, entries[0].Data.ToArray());
            Assert.AreEqual(new byte[] { 2 }, entries[1].Data.ToArray());
            Assert.AreEqual(1, SeqOf(entries[2]));
            Assert.AreEqual(7u, ObjectHeader.ReadObjectId(entries[2].Data.Span.Slice(SeqHeader.Length)));
            Assert.AreEqual(1, ObjectHeader.ReadBehaviourIndex(entries[2].Data.Span.Slice(SeqHeader.Length)));
            Assert.AreEqual(2, outbox.Reliable.InFlightCount);
            Assert.AreEqual(2, outbox.Reliable.NextSendSeq);

            outbox.Reliable.OnAck(2, 0, Now + TimeSpan.FromMilliseconds(40));

            Assert.AreEqual(0, outbox.Reliable.InFlightCount);
        }

        [Test]
        public void AckGoesFirstInTheFirstBundleAndOnlyOnce()
        {
            SessionOutbox outbox = Outbox();
            outbox.Reliable.Advance();
            outbox.Reliable.MarkReceived();
            outbox.Enqueue(10, new byte[] { 1 });
            var frames = new List<List<BundleEntry>>();
            MessageSender sender = (messageId, payload) =>
            {
                frames.Add(Entries(messageId, payload));
                return SendStatus.Sent;
            };

            outbox.Flush(sender, Now, 1, null);
            outbox.Flush(sender, Now, 1, null);

            Assert.AreEqual(1, frames.Count);
            Assert.AreEqual(TestBundles.AckId, frames[0][0].MessageId);
            var ack = new ReliableAck();
            ReliableAckNetAdapter.Instance.Decode(frames[0][0].Data, ref ack);
            Assert.AreEqual(1, ack.Next);
            Assert.AreEqual(10u, frames[0][1].MessageId);
            Assert.IsFalse(outbox.Reliable.AckPending);
        }

        [Test]
        public void UnackedMessageIsResentOnceItsRtoHasPassed()
        {
            SessionOutbox outbox = Outbox();
            outbox.Enqueue(OrderId, new byte[] { 9 });
            var frames = new List<List<BundleEntry>>();
            MessageSender sender = (messageId, payload) =>
            {
                frames.Add(Entries(messageId, payload));
                return SendStatus.Sent;
            };

            outbox.Flush(sender, Now, 1, null);
            outbox.Flush(sender, Now + ReliableChannel.InitialRto - TimeSpan.FromMilliseconds(1), 1, null);
            outbox.Flush(sender, Now + ReliableChannel.InitialRto, 1, null);

            Assert.AreEqual(2, frames.Count);
            Assert.AreEqual(OrderId, frames[1][0].MessageId);
            Assert.AreEqual(0, SeqOf(frames[1][0]));
            Assert.AreEqual(new byte[] { 0, 0, 9 }, frames[1][0].Data.ToArray());
            Assert.AreEqual(1, outbox.Reliable.InFlightCount);
        }

        [Test]
        public void FullWindowStopsTheFlushAtTheFirstReliableMessageItCannotSend()
        {
            SessionOutbox outbox = Outbox(window: 1);
            outbox.Enqueue(OrderId, new byte[] { 1 });
            outbox.Enqueue(10, new byte[] { 2 });
            outbox.Enqueue(OrderId, new byte[] { 3 });
            outbox.Enqueue(20, new byte[] { 4 });
            var frames = new List<uint[]>();

            outbox.Flush((messageId, payload) =>
            {
                frames.Add(EntryIds(messageId, payload));
                return SendStatus.Sent;
            }, Now, 1, null);

            Assert.AreEqual(new uint[] { OrderId, 10 }, frames[0]);
            Assert.AreEqual(2, outbox.Queue.Count);

            outbox.Reliable.OnAck(1, 0, Now);
            outbox.Flush((messageId, payload) =>
            {
                frames.Add(EntryIds(messageId, payload));
                return SendStatus.Sent;
            }, Now, 1, null);

            Assert.AreEqual(new uint[] { OrderId, 20 }, frames[1]);
            Assert.AreEqual(0, outbox.Queue.Count);
        }

        [Test]
        public void DroppedReliableMessageDoesNotUseUpASeq()
        {
            SessionOutbox outbox = Outbox();
            outbox.Enqueue(OrderId, new byte[] { 1, 1, 1, 1 });
            outbox.Enqueue(OrderId, new byte[] { 2 });
            var sent = new List<BundleEntry>();
            var dropped = new List<SendDroppedArgs>();

            outbox.Flush((messageId, payload) =>
            {
                List<BundleEntry> entries = Entries(messageId, payload);
                if (entries.Exists(entry => entry.Data.Length == SeqHeader.Length + 4))
                {
                    return SendStatus.TooLarge;
                }

                sent.AddRange(entries);
                return SendStatus.Sent;
            }, Now, 1, dropped.Add);

            Assert.AreEqual(1, dropped.Count);
            Assert.AreEqual(1, sent.Count);
            Assert.AreEqual(0, SeqOf(sent[0]));
            Assert.AreEqual(1, outbox.Reliable.NextSendSeq);
        }

        [Test]
        public void CongestedLeavesReliableMessagesQueuedWithoutUsingUpASeq()
        {
            SessionOutbox outbox = Outbox();
            outbox.Enqueue(OrderId, new byte[] { 1 });

            outbox.Flush((messageId, payload) => SendStatus.Congested, Now, 1, null);

            Assert.AreEqual(1, outbox.Queue.Count);
            Assert.AreEqual(0, outbox.Reliable.InFlightCount);
            Assert.AreEqual(0, outbox.Reliable.NextSendSeq);
        }

        [Test]
        public void ReliableHeaderCountsAgainstTheQueueCapacity()
        {
            SessionOutbox outbox = Outbox();

            Assert.AreEqual(EnqueueResult.Queued, outbox.Enqueue(OrderId, new byte[1]));
            Assert.AreEqual(SeqHeader.Length + 1, outbox.Queue.PendingBytes);
            Assert.AreEqual(EnqueueResult.Queued, outbox.EnqueueToObject(OrderId, 1, 0, new byte[1]));
            Assert.AreEqual(2 * SeqHeader.Length + ObjectHeader.UnreliableLength + 2, outbox.Queue.PendingBytes);
        }

        [Test]
        public void BundleThroughAFomoxaConnectionOverLoopback()
        {
            var loopback = new LoopbackListener(64);
            var schema = new Schema(0xCAFE, new[] { new MessageSchema(0x3000_0001, 0xF00D, new ulong[] { 0xF00D }) });
            using var server = new FomoxaServer(loopback, schema, new SessionConfig());
            using var client = FomoxaConnection.Connect(loopback.Connect(), schema, new SessionConfig(), TimeSpan.Zero);

            TimeSpan now = TimeSpan.Zero;
            for (int step = 0; step < 20 && !client.IsReady; step++)
            {
                now += TimeSpan.FromMilliseconds(16);
                server.Tick(now);
                client.Tick(now);
            }

            SessionOutbox outbox = Outbox();
            outbox.Enqueue(0x3000_0001, new byte[] { 7 });
            outbox.Enqueue(0x3000_0001, new byte[] { 8 });
            outbox.Flush(client.Send, now, 0, null);

            now += TimeSpan.FromMilliseconds(16);
            var reader = new BundleReader(TestBundles.Format());
            var received = new List<byte>();
            foreach (FomoxaEvent raised in server.Tick(now))
            {
                if (raised.Kind == FomoxaEventKind.Message)
                {
                    Assert.IsTrue(reader.TryRead(raised.MessageId, raised.Payload));
                    for (int index = 0; index < reader.Count; index++)
                    {
                        received.Add(reader.DataAt(index).Span[0]);
                    }
                }
            }

            Assert.AreEqual(new byte[] { 7, 8 }, received.ToArray());
        }
    }
}
