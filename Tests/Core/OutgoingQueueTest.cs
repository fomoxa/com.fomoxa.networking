using System;
using System.Collections.Generic;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class OutgoingQueueTest
    {
        [Test]
        public void FullQueueRefusesWithoutDroppingQueuedMessages()
        {
            var queue = new OutgoingQueue(2, 1024);

            Assert.AreEqual(EnqueueResult.Queued, queue.Enqueue(1, new byte[] { 1 }));
            Assert.AreEqual(EnqueueResult.Queued, queue.Enqueue(2, new byte[] { 2 }));
            Assert.AreEqual(EnqueueResult.Full, queue.Enqueue(3, new byte[] { 3 }));
            Assert.AreEqual(2, queue.Count);
        }

        [Test]
        public void UnboundedMessagesPassBothCapsButCountTowardThem()
        {
            var queue = new OutgoingQueue(2, 16);

            Assert.AreEqual(EnqueueResult.Queued, queue.EnqueueUnbounded(1, ReadOnlySpan<byte>.Empty, new byte[10]));
            Assert.AreEqual(EnqueueResult.Queued, queue.EnqueueUnbounded(2, ReadOnlySpan<byte>.Empty, new byte[10]));
            Assert.AreEqual(EnqueueResult.Queued, queue.EnqueueUnbounded(3, ReadOnlySpan<byte>.Empty, new byte[20]));
            Assert.AreEqual(3, queue.Count);
            Assert.AreEqual(40, queue.PendingBytes);
            Assert.AreEqual(EnqueueResult.Full, queue.Enqueue(4, new byte[] { 4 }));
            Assert.AreEqual(EnqueueResult.TooLarge, queue.EnqueueUnbounded(5, ReadOnlySpan<byte>.Empty, new byte[FomoxaWire.MaxMessagePayload + 1]));
        }

        [Test]
        public void MessagesMovedAsideAndAppendedBackFollowTheMessagesQueuedInBetween()
        {
            var queue = new OutgoingQueue(8, 1024);
            queue.Enqueue(1, new byte[] { 1 });
            queue.Enqueue(2, new byte[] { 2, 2 });
            var held = new List<QueuedMessage>();

            queue.MoveAllTo(held);
            Assert.AreEqual(0, queue.Count);
            Assert.AreEqual(0, queue.PendingBytes);
            queue.Enqueue(3, new byte[] { 3 });
            queue.AppendAll(held);

            CollectionAssert.AreEqual(new uint[] { 3, 1, 2 }, QueuedIds(queue));
            Assert.AreEqual(4, queue.PendingBytes);
        }

        [Test]
        public void PayloadOverTheFomoxaLimitIsRefusedBeforeEveryQueueCheck()
        {
            var queue = new OutgoingQueue(1, 1024);
            queue.Enqueue(1, new byte[] { 1 });

            Assert.AreEqual(EnqueueResult.TooLarge, queue.Enqueue(2, new byte[FomoxaWire.MaxMessagePayload + 1]));
            Assert.AreEqual(1, queue.Count);
        }

        [Test]
        public void PayloadThatLeavesNoRoomForTheBundleFramingIsRefused()
        {
            var queue = new OutgoingQueue(1, FomoxaWire.MaxMessagePayload);

            Assert.AreEqual(EnqueueResult.TooLarge, queue.Enqueue(1, new byte[BundleFormat.MaxEntryPayload + 1]));
        }

        [Test]
        public void PayloadAtTheBundleEntryLimitIsQueuedWhenTheByteCapacityAllowsIt()
        {
            var queue = new OutgoingQueue(1, FomoxaWire.MaxMessagePayload);

            Assert.AreEqual(EnqueueResult.Queued, queue.Enqueue(1, new byte[BundleFormat.MaxEntryPayload]));
        }

        [Test]
        public void PayloadLargerThanTheByteCapacityIsRefusedEvenWhenTheQueueIsEmpty()
        {
            var queue = new OutgoingQueue(4, 3);

            Assert.AreEqual(EnqueueResult.TooLargeForQueue, queue.Enqueue(1, new byte[4]));
            Assert.AreEqual(0, queue.Count);
        }

        [Test]
        public void PendingBytesOverTheByteCapacityAreRefusedUntilTheQueueDrains()
        {
            var queue = new OutgoingQueue(4, 3);

            Assert.AreEqual(EnqueueResult.Queued, queue.Enqueue(1, new byte[2]));
            Assert.AreEqual(EnqueueResult.BytesFull, queue.Enqueue(2, new byte[2]));
            Assert.AreEqual(EnqueueResult.Queued, queue.Enqueue(3, new byte[1]));
            Assert.AreEqual(3, queue.PendingBytes);

            queue.Dequeue(queue.Count);
            Assert.AreEqual(0, queue.PendingBytes);
            Assert.AreEqual(EnqueueResult.Queued, queue.Enqueue(4, new byte[2]));
        }

        [Test]
        public void DropNextRemovesTheHeadAndReportsIt()
        {
            var queue = new OutgoingQueue(4, 1024);
            queue.Enqueue(10, new byte[] { 1, 2, 3 });
            queue.Enqueue(20, new byte[] { 4 });

            Assert.IsTrue(queue.TryDropNext(out uint messageId, out int payloadLength));
            Assert.AreEqual(10u, messageId);
            Assert.AreEqual(3, payloadLength);

            Assert.AreEqual(new uint[] { 20 }, QueuedIds(queue));
            Assert.AreEqual(1, queue.PendingBytes);
        }

        [Test]
        public void ClearReportsHowManyMessagesItRemoved()
        {
            var queue = new OutgoingQueue(4, 1024);
            queue.Enqueue(10, new byte[] { 1 });
            queue.Enqueue(20, new byte[] { 2 });

            Assert.AreEqual(2, queue.Clear());
            Assert.AreEqual(0, queue.Count);
            Assert.AreEqual(0, queue.PendingBytes);
            Assert.AreEqual(0, queue.Clear());
        }

        [Test]
        public void EnumerationFollowsEnqueueOrderAndDequeueRemovesFromTheHead()
        {
            var queue = new OutgoingQueue(4, 1024);
            queue.Enqueue(10, new byte[] { 1 });
            queue.Enqueue(20, new byte[] { 2, 3 });
            queue.Enqueue(30, new byte[] { 4 });

            Assert.AreEqual(new uint[] { 10, 20, 30 }, QueuedIds(queue));

            queue.Dequeue(2);

            Assert.AreEqual(new uint[] { 30 }, QueuedIds(queue));
            Assert.AreEqual(1, queue.PendingBytes);
            Assert.Throws<ArgumentOutOfRangeException>(() => queue.Dequeue(2));
        }

        [Test]
        public void HeaderAndPayloadAreStoredAsOneEntryAndCountTogether()
        {
            var queue = new OutgoingQueue(4, 6);

            Assert.AreEqual(EnqueueResult.TooLargeForQueue, queue.Enqueue(1, new byte[] { 1, 2, 3 }, new byte[] { 4, 5, 6, 7 }));
            Assert.AreEqual(EnqueueResult.Queued, queue.Enqueue(1, new byte[] { 1, 2 }, new byte[] { 3 }));
            Assert.AreEqual(3, queue.PendingBytes);
            Assert.AreEqual(EnqueueResult.BytesFull, queue.Enqueue(2, new byte[] { 1, 2 }, new byte[] { 3, 4 }));

            foreach (QueuedMessage message in queue)
            {
                Assert.AreEqual(new byte[] { 1, 2, 3 }, message.Payload);
            }
        }

        [Test]
        public void HeaderCountsAgainstTheBundleEntryLimit()
        {
            var queue = new OutgoingQueue(1, FomoxaWire.MaxMessagePayload);
            var header = new byte[ObjectHeader.UnreliableLength];

            Assert.AreEqual(EnqueueResult.TooLarge, queue.Enqueue(1, header, new byte[BundleFormat.MaxEntryPayload - header.Length + 1]));
            Assert.AreEqual(EnqueueResult.Queued, queue.Enqueue(1, header, new byte[BundleFormat.MaxEntryPayload - header.Length]));
        }

        [Test]
        public void EnqueuedPayloadIsACopy()
        {
            var queue = new OutgoingQueue(1, 1024);
            var source = new byte[] { 5 };
            queue.Enqueue(1, source);
            source[0] = 9;

            foreach (QueuedMessage message in queue)
            {
                Assert.AreEqual(5, message.Payload[0]);
            }
        }

        private static uint[] QueuedIds(OutgoingQueue queue)
        {
            var ids = new List<uint>();
            foreach (QueuedMessage message in queue)
            {
                ids.Add(message.MessageId);
            }

            return ids.ToArray();
        }
    }
}
