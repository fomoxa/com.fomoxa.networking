using System;
using BundleFixture;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class ZeroRunsTest
    {
        [Test]
        public void IdenticalBytesEncodeToNothing()
        {
            byte[] output = new byte[0];

            Assert.AreEqual(0, ZeroRuns.Encode(new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 3 }, ref output));
        }

        [Test]
        public void OneChangedByteIsOneSegmentAndTrailingZerosCostNothing()
        {
            byte[] baseline = new byte[64];
            byte[] current = new byte[64];
            current[10] = 0x0F;

            byte[] data = Encoded(current, baseline);

            CollectionAssert.AreEqual(new byte[] { 10, 1, 0x0F }, data);
        }

        [Test]
        public void ShortZeroGapsStayInsideTheLiteralsAndLongOnesStartANewSegment()
        {
            byte[] baseline = new byte[12];
            byte[] current = { 1, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 0 };

            byte[] data = Encoded(current, baseline);

            CollectionAssert.AreEqual(new byte[] { 0, 4, 1, 0, 0, 2, 3, 1, 3 }, data);
        }

        [Test]
        public void RunsLongerThan255AreSplit()
        {
            byte[] baseline = new byte[700];
            byte[] current = new byte[700];
            current[600] = 1;
            for (int index = 300; index < 600; index++)
            {
                current[index] = 0xFF;
            }

            byte[] data = Encoded(current, baseline);

            Assert.AreEqual(255, data[0]);
            Assert.AreEqual(0, data[1]);
            Assert.AreEqual(45, data[2]);
            Assert.AreEqual(255, data[3]);
            Assert.AreEqual(0, data[2 + 2 + 255]);
            Assert.AreEqual(46, data[2 + 2 + 255 + 1]);
            Assert.AreEqual(3 * 2 + 255 + 46, data.Length);
            Assert.AreEqual(current, Applied(data, baseline));
        }

        [Test]
        public void ApplyingTheEncodedDeltaRebuildsTheCurrentBytes()
        {
            var random = new Random(7);
            for (int round = 0; round < 200; round++)
            {
                byte[] baseline = new byte[random.Next(0, 600)];
                random.NextBytes(baseline);
                byte[] current = (byte[])baseline.Clone();
                int changes = random.Next(0, 40);
                for (int change = 0; change < changes && current.Length > 0; change++)
                {
                    current[random.Next(current.Length)] = (byte)random.Next(256);
                }

                Assert.AreEqual(current, Applied(Encoded(current, baseline), baseline), $"round {round}");
            }
        }

        [Test]
        public void DataThatRunsPastTheTargetOrIsCutShortIsRejectedWithoutWriting()
        {
            byte[] target = { 1, 2, 3, 4 };

            Assert.IsFalse(ZeroRuns.TryApply(new byte[] { 3, 2, 9, 9 }, target));
            Assert.IsFalse(ZeroRuns.TryApply(new byte[] { 0, 1, 9, 5 }, target));
            Assert.IsFalse(ZeroRuns.TryApply(new byte[] { 0, 3, 9 }, target));
            Assert.IsFalse(ZeroRuns.TryApply(new byte[] { 255, 0 }, target));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, target);
            Assert.IsTrue(ZeroRuns.TryApply(new byte[] { 2, 2, 1, 1 }, target));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 2, 5 }, target);
        }

        [Test]
        public void BaselineOfAnotherLengthIsRefused()
        {
            byte[] output = new byte[0];

            Assert.Throws<ArgumentException>(() => ZeroRuns.Encode(new byte[2], new byte[3], ref output));
        }

        [Test]
        public void StateModelsOffTheReliableChannelAreRejected()
        {
            Assert.DoesNotThrow(() => TestObjects.StateProtocol(TestObjects.Channels()));
            Assert.Throws<ArgumentException>(() => TestObjects.StateProtocol(new MessageChannels()));
        }

        private static byte[] Encoded(byte[] current, byte[] baseline)
        {
            byte[] output = new byte[0];
            int length = ZeroRuns.Encode(current, baseline, ref output);
            return output.AsSpan(0, length).ToArray();
        }

        private static byte[] Applied(byte[] data, byte[] baseline)
        {
            byte[] target = (byte[])baseline.Clone();
            Assert.IsTrue(ZeroRuns.TryApply(data, target));
            return target;
        }
    }
}
