using System;
using Fomoxa.Networking.Messaging;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class ReliableChannelTest
    {
        private static readonly TimeSpan Start = TimeSpan.FromSeconds(1);

        [Test]
        public void ClassifyUsesTheModularDistanceFromTheNextExpectedSeq()
        {
            var channel = new ReliableChannel(1024);

            Assert.AreEqual(ReliableVerdict.Deliver, channel.Classify(0));
            Assert.AreEqual(ReliableVerdict.Buffer, channel.Classify(5));
            Assert.AreEqual(ReliableVerdict.Buffer, channel.Classify(ReliableChannel.MaxWindow - 1));
            Assert.AreEqual(ReliableVerdict.OutOfWindow, channel.Classify(ReliableChannel.MaxWindow));
            Assert.AreEqual(ReliableVerdict.Duplicate, channel.Classify(ushort.MaxValue));

            channel.Store(5, 10, new byte[] { 1 });

            Assert.AreEqual(ReliableVerdict.Duplicate, channel.Classify(5));
        }

        [Test]
        public void ClassifyWrapsAroundAfterSeq65535()
        {
            var channel = new ReliableChannel(1024);
            for (int step = 0; step < ushort.MaxValue; step++)
            {
                channel.Advance();
            }

            Assert.AreEqual(ReliableVerdict.Deliver, channel.Classify(ushort.MaxValue));
            Assert.AreEqual(ReliableVerdict.Buffer, channel.Classify(0));
            Assert.AreEqual(ReliableVerdict.Duplicate, channel.Classify(ushort.MaxValue - 1));
        }

        [Test]
        public void BufferedMessagesAreTakenInOrderOnceTheGapIsFilled()
        {
            var channel = new ReliableChannel(1024);
            channel.Store(2, 20, new byte[] { 2 });
            channel.Store(1, 10, new byte[] { 1 });

            Assert.IsFalse(channel.TryTakeNext(out _, out _));

            channel.Advance();

            Assert.IsTrue(channel.TryTakeNext(out uint first, out byte[] firstData));
            Assert.IsTrue(channel.TryTakeNext(out uint second, out _));
            Assert.IsFalse(channel.TryTakeNext(out _, out _));
            Assert.AreEqual(10u, first);
            Assert.AreEqual(new byte[] { 1 }, firstData);
            Assert.AreEqual(20u, second);
            Assert.AreEqual(3, channel.NextExpected);
            Assert.AreEqual(0, channel.BufferedCount);
        }

        [Test]
        public void StoredDataIsACopy()
        {
            var channel = new ReliableChannel(1024);
            var source = new byte[] { 4 };
            channel.Store(1, 10, source);
            source[0] = 9;
            channel.Advance();

            Assert.IsTrue(channel.TryTakeNext(out _, out byte[] data));
            Assert.AreEqual(new byte[] { 4 }, data);
        }

        [Test]
        public void AckReportsTheNextSeqAndTheBufferedSeqsWithinTheMask()
        {
            var channel = new ReliableChannel(1024);
            channel.Store(1, 10, new byte[0]);
            channel.Store(2, 10, new byte[0]);
            channel.Store(33, 10, new byte[0]);
            var ack = new ReliableAck();

            channel.WriteAck(ack);

            Assert.AreEqual(0, ack.Next);
            Assert.AreEqual(0b11u, ack.Received);
        }

        [Test]
        public void AckRemovesEverySeqBeforeNextAndEveryMaskedSeq()
        {
            var channel = new ReliableChannel(1024);
            for (int seq = 0; seq < 4; seq++)
            {
                channel.Track(10, new byte[2], Start);
            }

            channel.OnAck(1, 0b10, Start);

            Assert.AreEqual(2, channel.InFlightCount);
            Assert.AreEqual(4, channel.NextSendSeq);
        }

        [Test]
        public void FirstRttSampleSetsTheRtoFromRfc6298()
        {
            var channel = new ReliableChannel(1024);
            channel.Track(10, new byte[2], Start);

            channel.OnAck(1, 0, Start + TimeSpan.FromMilliseconds(100));

            Assert.AreEqual(TimeSpan.FromMilliseconds(300), channel.Rto);
        }

        [Test]
        public void RtoIsClampedBetween50MillisecondsAndOneSecond()
        {
            var fast = new ReliableChannel(1024);
            fast.Track(10, new byte[2], Start);
            fast.OnAck(1, 0, Start + TimeSpan.FromMilliseconds(1));
            var slow = new ReliableChannel(1024);
            slow.Track(10, new byte[2], Start);
            slow.OnAck(1, 0, Start + TimeSpan.FromSeconds(2));

            Assert.AreEqual(ReliableChannel.MinRto, fast.Rto);
            Assert.AreEqual(ReliableChannel.MaxRto, slow.Rto);
        }

        [Test]
        public void ResentMessageGivesNoRttSample()
        {
            var channel = new ReliableChannel(1024);
            channel.Track(10, new byte[2], Start);
            channel.MarkResent(0, Start + ReliableChannel.InitialRto);

            channel.OnAck(1, 0, Start + TimeSpan.FromSeconds(5));

            Assert.AreEqual(ReliableChannel.InitialRto, channel.Rto);
            Assert.AreEqual(0, channel.InFlightCount);
        }

        [Test]
        public void WindowIsBetweenOneAndTheMaximum()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ReliableChannel(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ReliableChannel(ReliableChannel.MaxWindow + 1));

            var channel = new ReliableChannel(2);
            channel.Track(10, new byte[2], Start);

            Assert.IsTrue(channel.CanSend(0));
            Assert.IsFalse(channel.CanSend(1));
        }
    }
}
