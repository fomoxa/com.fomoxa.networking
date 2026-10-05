using System;
using System.Collections.Generic;
using Fomoxa.Networking.Objects;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class ObjectTableTest
    {
        [Test]
        public void DuplicateIdIsRefusedWithoutChangingTheTable()
        {
            var table = new ObjectTable();
            Assert.IsTrue(table.TryAdd(new ObjectRow(1, 10, 0)));
            Assert.IsTrue(table.TryAdd(new ObjectRow(2, 20, 0)));

            Assert.IsFalse(table.TryAdd(new ObjectRow(1, 30, 5)));
            Assert.Throws<ArgumentException>(() => table.Add(new ObjectRow(2, 40, 0)));

            Assert.AreEqual(2, table.Count);
            Assert.IsTrue(table.TryGet(1, out ObjectRow row));
            Assert.AreEqual(10u, row.PrefabId);
            Assert.AreEqual(0UL, row.OwnerId);
            var ids = new List<uint>();
            table.CopyIdsTo(ids);
            CollectionAssert.AreEqual(new[] { 1u, 2u }, ids);
        }

        [Test]
        public void SetOwnerKeepsTheSceneObjectId()
        {
            var table = new ObjectTable();
            table.Add(new ObjectRow(1, 0, 0xABCDUL, 0));

            Assert.IsTrue(table.SetOwner(1, 5, out ulong previousOwnerId));

            Assert.AreEqual(0UL, previousOwnerId);
            Assert.IsTrue(table.TryGet(1, out ObjectRow row));
            Assert.AreEqual(0xABCDUL, row.SceneObjectId);
            Assert.AreEqual(5UL, row.OwnerId);
        }
    }
}
