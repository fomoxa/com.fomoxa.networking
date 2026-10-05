using System;
using System.Runtime.InteropServices;
using BundleFixture;
using Fomoxa.Networking.Messaging;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class BundleReaderTest
    {
        private static byte[] Encode(params (uint MessageId, byte[] Data)[] entries)
        {
            var bundle = new MessageBundle();
            foreach ((uint messageId, byte[] data) in entries)
            {
                bundle.Entries.Add(new BundleEntry { MessageId = messageId, Data = data });
            }

            return MessageBundleNetAdapter.Instance.Encode(bundle).ToArray();
        }

        [Test]
        public void ReadsEveryEntryInOrder()
        {
            var reader = new BundleReader(TestBundles.Format());
            byte[] frame = Encode((10, new byte[] { 1, 2 }), (20, new byte[0]), (30, new byte[] { 3 }));

            Assert.IsTrue(reader.TryRead(TestBundles.MessageId, frame));
            Assert.AreEqual(3, reader.Count);
            Assert.AreEqual(10u, reader.MessageIdAt(0));
            Assert.AreEqual(new byte[] { 1, 2 }, reader.DataAt(0).ToArray());
            Assert.AreEqual(0, reader.DataAt(1).Length);
            Assert.AreEqual(30u, reader.MessageIdAt(2));
        }

        [Test]
        public void EntryDataIsASliceOfTheFramePayload()
        {
            var reader = new BundleReader(TestBundles.Format());
            byte[] frame = Encode((10, new byte[] { 1, 2 }));

            Assert.IsTrue(reader.TryRead(TestBundles.MessageId, frame));
            Assert.IsTrue(MemoryMarshal.TryGetArray(reader.DataAt(0), out ArraySegment<byte> segment));
            Assert.AreSame(frame, segment.Array);
        }

        [Test]
        public void ReleaseDropsTheSlicesSoNothingOutlivesTheTick()
        {
            var reader = new BundleReader(TestBundles.Format());
            Assert.IsTrue(reader.TryRead(TestBundles.MessageId, Encode((10, new byte[] { 1 }))));

            reader.Release();

            Assert.AreEqual(0, reader.DataAt(0).Length);
        }

        [Test]
        public void FrameWithAnotherMessageIdIsNotABundle()
        {
            var reader = new BundleReader(TestBundles.Format());

            Assert.IsFalse(reader.TryRead(TestBundles.MessageId + 1, Encode((10, new byte[] { 1 }))));
            Assert.AreEqual(0, reader.Count);
        }

        [Test]
        public void TruncatedBundleIsRefused()
        {
            var reader = new BundleReader(TestBundles.Format());
            byte[] frame = Encode((10, new byte[] { 1, 2, 3 }));

            Assert.IsFalse(reader.TryRead(TestBundles.MessageId, frame.AsMemory(0, frame.Length - 1)));
            Assert.AreEqual(0, reader.Count);
        }

        [Test]
        public void ReadingAgainReusesTheEntries()
        {
            var reader = new BundleReader(TestBundles.Format());
            byte[] first = Encode((10, new byte[] { 1 }), (20, new byte[] { 2 }));
            byte[] second = Encode((30, new byte[] { 3 }));

            Assert.IsTrue(reader.TryRead(TestBundles.MessageId, first));
            Assert.IsTrue(reader.TryRead(TestBundles.MessageId, second));

            Assert.AreEqual(1, reader.Count);
            Assert.AreEqual(30u, reader.MessageIdAt(0));
        }
    }
}
