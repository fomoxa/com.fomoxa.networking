using System;
using System.Collections.Generic;
using System.Numerics;
using BundleFixture;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class SpawnTransformTest
    {
        private static ObjectSpawn Packed(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var spawn = new ObjectSpawn();
            SpawnTransform.Pack(new SpawnData(0, position, rotation, scale), spawn);
            return spawn;
        }

        [Test]
        public void IdentityTransformCostsTheMaskAndAnEmptyArray()
        {
            ObjectSpawn identity = Packed(Vector3.Zero, Quaternion.Identity, Vector3.One);
            ObjectSpawn full = Packed(new Vector3(1, 2, 3), new Quaternion(0, 1, 0, 0), new Vector3(2, 2, 2));

            Assert.AreEqual(0, identity.Mask);
            Assert.AreEqual(0, identity.Values.Count);
            Assert.AreEqual(33, ObjectSpawnNetAdapter.Instance.Encode(identity).Length);
            Assert.AreEqual(73, ObjectSpawnNetAdapter.Instance.Encode(full).Length);
        }

        [Test]
        public void PackSelectedWritesOnlyTheMaskedComponentsInFixedOrder()
        {
            var values = new List<float> { 99 };

            SpawnTransform.PackSelected(SpawnTransform.RotationBit | SpawnTransform.ScaleBit, new Vector3(1, 2, 3), new Quaternion(4, 5, 6, 7), new Vector3(8, 9, 10), values);

            CollectionAssert.AreEqual(new[] { 4f, 5f, 6f, 7f, 8f, 9f, 10f }, values);
            Assert.IsTrue(SpawnTransform.TryUnpack(SpawnTransform.RotationBit | SpawnTransform.ScaleBit, values, out Vector3 position, out Quaternion rotation, out Vector3 scale));
            Assert.AreEqual(Vector3.Zero, position);
            Assert.AreEqual(new Quaternion(4, 5, 6, 7), rotation);
            Assert.AreEqual(new Vector3(8, 9, 10), scale);
            Assert.Throws<ArgumentException>(() => SpawnTransform.PackSelected(8, Vector3.Zero, Quaternion.Identity, Vector3.One, values));
        }

        [Test]
        public void TransformSettleOffTheReliableChannelIsRejected()
        {
            Assert.DoesNotThrow(() => TestObjects.TransformProtocol(TestObjects.Channels()));
            Assert.Throws<ArgumentException>(() => TestObjects.TransformProtocol(new MessageChannels()));
        }

        [Test]
        public void EachStateCostsItsLengthPrefixAndItsBytes()
        {
            ObjectSpawn spawn = Packed(Vector3.Zero, Quaternion.Identity, Vector3.One);
            SpawnTransform.PackStates(new[] { ReadOnlyMemory<byte>.Empty, new ReadOnlyMemory<byte>(new byte[] { 1, 2 }) }, spawn.States);

            Assert.AreEqual(2, spawn.States.Count);
            Assert.AreEqual(33 + 4 + 4 + 2, ObjectSpawnNetAdapter.Instance.Encode(spawn).Length);
        }

        [Test]
        public void OnlyComponentsThatDifferFromTheirConstantAreSentInFixedOrder()
        {
            ObjectSpawn positionAndScale = Packed(new Vector3(1, 2, 3), Quaternion.Identity, new Vector3(4, 5, 6));
            ObjectSpawn all = Packed(new Vector3(1, 2, 3), new Quaternion(4, 5, 6, 7), new Vector3(8, 9, 10));

            Assert.AreEqual(SpawnTransform.PositionBit | SpawnTransform.ScaleBit, positionAndScale.Mask);
            CollectionAssert.AreEqual(new[] { 1f, 2f, 3f, 4f, 5f, 6f }, positionAndScale.Values);
            Assert.AreEqual(SpawnTransform.AllBits, all.Mask);
            CollectionAssert.AreEqual(new[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f }, all.Values);
        }

        [Test]
        public void UnpackFillsAbsentComponentsWithTheirConstants()
        {
            ObjectSpawn rotationOnly = Packed(Vector3.Zero, new Quaternion(0, 1, 0, 0), Vector3.One);

            Assert.IsTrue(SpawnTransform.TryUnpack(rotationOnly.Mask, rotationOnly.Values, out Vector3 position, out Quaternion rotation, out Vector3 scale));
            Assert.AreEqual(Vector3.Zero, position);
            Assert.AreEqual(new Quaternion(0, 1, 0, 0), rotation);
            Assert.AreEqual(Vector3.One, scale);
        }

        [TestCase((byte)8, 0)]
        [TestCase((byte)1, 2)]
        [TestCase((byte)1, 4)]
        [TestCase((byte)7, 9)]
        [TestCase((byte)0, 1)]
        public void UnpackRejectsUnknownBitsAndValueCountsThatDoNotMatchTheMask(byte mask, int valueCount)
        {
            var spawn = new ObjectSpawn { Mask = mask };
            for (int index = 0; index < valueCount; index++)
            {
                spawn.Values.Add(index);
            }

            Assert.IsFalse(SpawnTransform.TryUnpack(spawn.Mask, spawn.Values, out _, out _, out _));
        }
    }
}
