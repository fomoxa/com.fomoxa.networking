using System;
using System.Collections.Generic;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class AuthenticationQueueTest
    {
        private const uint OrderId = 40;
        private static readonly TimeSpan Now = TimeSpan.FromSeconds(10);

        private static SessionOutbox Outbox(int capacity = 64, int frameBudget = FomoxaWire.MaxDataFrameSize)
        {
            var channels = new MessageChannels();
            channels.Set(OrderId, Channel.ReliableOrdered);
            return new SessionOutbox(new OutgoingQueue(capacity, 1024 * 1024), TestBundles.Protocol(channels, frameBudget), new ReliableChannel(1024));
        }

        private static List<uint> FlushedIds(SessionOutbox outbox)
        {
            var ids = new List<uint>();
            outbox.Flush((messageId, payload) =>
            {
                ids.AddRange(EntryIds(messageId, payload));
                return SendStatus.Sent;
            }, Now, 7, null);
            return ids;
        }

        private static List<uint> EntryIds(uint frameMessageId, ReadOnlySpan<byte> frame)
        {
            Assert.AreEqual(TestBundles.MessageId, frameMessageId);
            var bundle = new MessageBundle();
            MessageBundleNetAdapter.Instance.Decode(frame.ToArray(), ref bundle);
            return bundle.Entries.ConvertAll(entry => entry.MessageId);
        }

        [Test]
        public void AGatedFlushSendsOnlyTheAuthenticatorQueue()
        {
            SessionOutbox outbox = Outbox();
            outbox.Enqueue(10, new byte[] { 1 });
            outbox.Gate();
            outbox.EnqueueAuthentication(20, new byte[] { 2 });

            CollectionAssert.AreEqual(new uint[] { 20 }, FlushedIds(outbox));
            Assert.AreEqual(1, outbox.Queue.Count);
            Assert.AreEqual(0, outbox.ActiveCount);
        }

        [Test]
        public void AGatedFlushWithNothingFromTheAuthenticatorSendsNothing()
        {
            SessionOutbox outbox = Outbox();
            outbox.Enqueue(10, new byte[] { 1 });
            outbox.Gate();

            CollectionAssert.IsEmpty(FlushedIds(outbox));
            Assert.AreEqual(0, outbox.ActiveCount);
            Assert.AreEqual(1, outbox.Queue.Count);
        }

        [Test]
        public void TheAuthenticatorQueueHasTheSameCapsCountedSeparately()
        {
            SessionOutbox outbox = Outbox(capacity: 2);
            outbox.Enqueue(10, new byte[] { 1 });
            outbox.Enqueue(10, new byte[] { 2 });
            outbox.Gate();

            Assert.AreEqual(EnqueueResult.Full, outbox.Enqueue(10, new byte[] { 3 }));
            Assert.AreEqual(EnqueueResult.Queued, outbox.EnqueueAuthentication(20, new byte[] { 4 }));
            Assert.AreEqual(EnqueueResult.Queued, outbox.EnqueueAuthentication(20, new byte[] { 5 }));
            Assert.AreEqual(EnqueueResult.Full, outbox.EnqueueAuthentication(20, new byte[] { 6 }));
        }

        [Test]
        public void KeptLeftoversGoAheadOfTheMainQueue()
        {
            SessionOutbox outbox = Outbox();
            outbox.Enqueue(10, new byte[] { 1 });
            outbox.Gate();
            outbox.EnqueueAuthentication(20, new byte[] { 2 });
            outbox.EnqueueAuthentication(30, new byte[] { 3 });

            outbox.OpenGate(true);

            Assert.IsFalse(outbox.IsGated);
            CollectionAssert.AreEqual(new uint[] { 20, 30, 10 }, FlushedIds(outbox));
        }

        [Test]
        public void DroppedLeftoversLeaveOnlyTheMainQueue()
        {
            SessionOutbox outbox = Outbox();
            outbox.Enqueue(10, new byte[] { 1 });
            outbox.Gate();
            outbox.EnqueueAuthentication(20, new byte[] { 2 });

            outbox.OpenGate(false);

            CollectionAssert.AreEqual(new uint[] { 10 }, FlushedIds(outbox));
        }

        [Test]
        public void DroppingLeftoversKeepsAMessageWhoseFragmentsAreAlreadyOnTheWire()
        {
            SessionOutbox outbox = Outbox(frameBudget: 300);
            outbox.Enqueue(10, new byte[] { 1 });
            outbox.Gate();
            outbox.EnqueueAuthentication(OrderId, new byte[1000]);
            outbox.EnqueueAuthentication(20, new byte[] { 2 });
            int sent = 0;
            outbox.Flush((messageId, payload) => sent++ == 0 ? SendStatus.Sent : SendStatus.Congested, Now, 7, null);

            outbox.OpenGate(false);
            List<uint> ids = FlushedIds(outbox);

            Assert.AreEqual(TestBundles.FragmentPartId, ids[0]);
            CollectionAssert.DoesNotContain(ids, TestBundles.FragmentStartId);
            CollectionAssert.DoesNotContain(ids, 20u);
            Assert.AreEqual(10u, ids[ids.Count - 1]);
        }

        [Test]
        public void ASessionMessageGoesToTheQueueThatIsFlushed()
        {
            SessionOutbox gated = Outbox();
            gated.Enqueue(10, new byte[] { 1 });
            gated.Gate();
            gated.EnqueueActiveUnbounded(TestBundles.CloseId, new byte[] { 3 });
            SessionOutbox open = Outbox();
            open.Enqueue(10, new byte[] { 1 });
            open.EnqueueActiveUnbounded(TestBundles.CloseId, new byte[] { 3 });

            CollectionAssert.AreEqual(new[] { TestBundles.CloseId }, FlushedIds(gated));
            CollectionAssert.AreEqual(new[] { 10u, TestBundles.CloseId }, FlushedIds(open));
        }

        [Test]
        public void ClearingEmptiesBothQueues()
        {
            SessionOutbox outbox = Outbox();
            outbox.Enqueue(10, new byte[] { 1 });
            outbox.Gate();
            outbox.EnqueueAuthentication(20, new byte[] { 2 });
            outbox.EnqueueAuthentication(20, new byte[] { 3 });

            Assert.AreEqual(3, outbox.Clear());
            Assert.AreEqual(0, outbox.ActiveCount);
            Assert.AreEqual(0, outbox.Queue.Count);
        }
    }
}
