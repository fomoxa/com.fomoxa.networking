using System.Collections.Generic;
using System.Numerics;
using Fomoxa.Networking.Objects;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class EntityPoseTest
    {
        [Test]
        public void SpawnDataCarriesTheRootPoseAndTheStatesOfTheRepresentation()
        {
            var world = new ClientEntitiesTest.World();
            ClientEntitiesTest.FakeEntity served = world.Served();
            served.Position = new Vector3(1, 2, 3);
            served.Rotation = Quaternion.CreateFromYawPitchRoll(0.5f, 0, 0);
            served.Scale = new Vector3(2, 2, 2);
            world.Server.Spawn(served, 0);

            SpawnData data = world.Server.ReadSpawnData(served.Record.ObjectId);

            Assert.AreEqual(served.Record.Fingerprint, data.PrefabFingerprint);
            Assert.AreEqual(new Vector3(1, 2, 3), data.Position);
            Assert.AreEqual(Quaternion.CreateFromYawPitchRoll(0.5f, 0, 0), data.Rotation);
            Assert.AreEqual(new Vector3(2, 2, 2), data.Scale);
            Assert.AreEqual(1, data.States.Count);
        }

        [Test]
        public void AnchorsAreTheOwnedPositionsReadOncePerRound()
        {
            var world = new ClientEntitiesTest.World();
            ulong peerId = world.ClientObjects.LocalPeerId;
            ClientEntitiesTest.FakeEntity anchor = world.Served();
            anchor.Position = new Vector3(4, 0, 0);
            world.Server.Spawn(anchor, peerId);

            IReadOnlyList<Vector3> first = world.Server.AnchorsOf(peerId);
            anchor.Position = new Vector3(9, 0, 0);
            int reads = anchor.PositionReads;

            Assert.AreSame(first, world.Server.AnchorsOf(peerId));
            CollectionAssert.AreEqual(new[] { new Vector3(4, 0, 0) }, first);
            Assert.AreEqual(reads, anchor.PositionReads);

            world.Server.NextAnchorRound();

            CollectionAssert.AreEqual(new[] { new Vector3(9, 0, 0) }, world.Server.AnchorsOf(peerId));
            Assert.AreEqual(reads + 1, anchor.PositionReads);
        }

        [Test]
        public void ChangingTheOwnerStartsANewAnchorRound()
        {
            var world = new ClientEntitiesTest.World();
            ulong peerId = world.ClientObjects.LocalPeerId;
            ClientEntitiesTest.FakeEntity first = world.Served();
            first.Position = new Vector3(1, 0, 0);
            ClientEntitiesTest.FakeEntity second = world.Served();
            second.Position = new Vector3(2, 0, 0);
            world.Server.Spawn(first, peerId);
            world.Server.Spawn(second, 0);
            Assert.AreEqual(1, world.Server.AnchorsOf(peerId).Count);

            Assert.IsTrue(world.Server.ChangeOwner(second, peerId));

            CollectionAssert.AreEqual(new[] { new Vector3(1, 0, 0), new Vector3(2, 0, 0) }, world.Server.AnchorsOf(peerId));
        }

        [Test]
        public void APeerWithoutOwnedEntitiesHasNoAnchors()
        {
            var world = new ClientEntitiesTest.World();
            world.Server.Spawn(world.Served(), 0);

            Assert.AreEqual(0, world.Server.AnchorsOf(world.ClientObjects.LocalPeerId).Count);
        }
    }
}
