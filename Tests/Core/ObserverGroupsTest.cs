using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class ObserverGroupsTest
    {
        private const uint Room = 7;
        private const uint OtherRoom = 8;

        [Test]
        public void AnEntityAddedToAGroupBeforeItSpawnsNeverReachesAPeerOutsideTheGroup()
        {
            var world = new ClientEntitiesTest.World();
            ulong peerId = world.ClientObjects.LocalPeerId;
            ClientEntitiesTest.FakeEntity entity = world.Served();
            world.Server.Groups.Add(Room, entity);

            world.Server.Spawn(entity, 0);
            world.Run(5);

            Assert.IsFalse(world.Server.IsObserver(entity, peerId));
            Assert.AreEqual(0, world.Backend.Created.Count);

            world.Server.Groups.Add(Room, peerId);

            Assert.IsTrue(world.Server.IsObserver(entity, peerId));
            world.Run(5);
            Assert.AreEqual(1, world.Backend.Created.Count);
        }

        [Test]
        public void APeerThatLeavesTheGroupGetsTheDespawnOfItsEntities()
        {
            var world = new ClientEntitiesTest.World();
            ulong peerId = world.ClientObjects.LocalPeerId;
            world.Server.Groups.Add(Room, peerId);
            ClientEntitiesTest.FakeEntity entity = world.Served();
            world.Server.Groups.Add(Room, entity);
            world.Server.Spawn(entity, 0);
            world.Run(5);
            Assert.AreEqual(1, world.Backend.Created.Count);

            Assert.IsTrue(world.Server.Groups.Remove(Room, peerId));

            Assert.IsFalse(world.Server.IsObserver(entity, peerId));
            world.Run(5);
            Assert.AreEqual(1, world.Backend.Ended.Count);
            Assert.IsFalse(world.Server.Groups.Contains(Room, peerId));
        }

        [Test]
        public void AnEntityMovedIntoAGroupAfterItSpawnsLeavesPeersOutsideTheGroup()
        {
            var world = new ClientEntitiesTest.World();
            ulong peerId = world.ClientObjects.LocalPeerId;
            world.Server.Groups.Add(OtherRoom, peerId);
            ClientEntitiesTest.FakeEntity entity = world.Served();
            world.Server.Spawn(entity, 0);
            world.Run(5);
            Assert.IsTrue(world.Server.IsObserver(entity, peerId));

            world.Server.Groups.Add(Room, entity);

            Assert.IsFalse(world.Server.IsObserver(entity, peerId));
            world.Run(5);
            Assert.AreEqual(1, world.Backend.Ended.Count);
        }

        [Test]
        public void AnEntityTakenOutOfItsLastGroupReachesEveryPeer()
        {
            var world = new ClientEntitiesTest.World();
            ulong peerId = world.ClientObjects.LocalPeerId;
            world.Server.Groups.Add(OtherRoom, peerId);
            ClientEntitiesTest.FakeEntity entity = world.Served();
            world.Server.Groups.Add(Room, entity);
            world.Server.Groups.Add(OtherRoom, entity);
            world.Server.Spawn(entity, 0);
            Assert.IsTrue(world.Server.IsObserver(entity, peerId));

            Assert.IsTrue(world.Server.Groups.Remove(OtherRoom, entity));

            Assert.IsFalse(world.Server.IsObserver(entity, peerId));

            Assert.IsTrue(world.Server.Groups.Remove(Room, entity));

            Assert.IsTrue(world.Server.IsObserver(entity, peerId));
        }

        [Test]
        public void TheOwnerObservesItsEntityOutsideItsGroups()
        {
            var world = new ClientEntitiesTest.World();
            ulong peerId = world.ClientObjects.LocalPeerId;
            ClientEntitiesTest.FakeEntity entity = world.Served();
            world.Server.Groups.Add(Room, entity);

            world.Server.Spawn(entity, peerId);

            Assert.IsTrue(world.Server.IsObserver(entity, peerId));
        }

        [Test]
        public void EveryoneVisibilityStaysInsideTheGroup()
        {
            var world = new ClientEntitiesTest.World();
            ulong peerId = world.ClientObjects.LocalPeerId;
            ClientEntitiesTest.FakeEntity entity = world.Served();
            entity.Visibility = NetworkVisibility.Everyone;
            world.Server.Groups.Add(Room, entity);

            world.Server.Spawn(entity, 0);

            Assert.IsFalse(world.Server.IsObserver(entity, peerId));
        }

        [Test]
        public void TheRuleIsNotAskedAboutAPairOutsideTheGroup()
        {
            var world = new ClientEntitiesTest.World();
            var rule = new CountingRule(true);
            world.Server.ObserverRule = rule;
            ClientEntitiesTest.FakeEntity grouped = world.Served();
            world.Server.Groups.Add(Room, grouped);
            world.Server.Spawn(grouped, 0);
            world.Server.Spawn(world.Served(), 0);
            int afterSpawns = rule.Calls;

            world.Server.RebuildObservers(grouped);
            world.Server.RebuildObservers();

            Assert.AreEqual(1, afterSpawns);
            Assert.AreEqual(2, rule.Calls);
        }

        [Test]
        public void ARuleThatDoesNotRebuildPeriodicallyIsLeftAloneByTheRound()
        {
            var world = new ClientEntitiesTest.World();
            ulong peerId = world.ClientObjects.LocalPeerId;
            var rule = new CountingRule(false) { Observes = false };
            world.Server.ObserverRule = rule;
            ClientEntitiesTest.FakeEntity entity = world.Served();
            world.Server.Spawn(entity, 0);
            rule.Observes = true;

            for (int tick = 0; tick < 30; tick++)
            {
                world.Server.RebuildObserversRound();
            }

            Assert.IsFalse(world.Server.IsObserver(entity, peerId));

            world.Server.RebuildObservers(entity);

            Assert.IsTrue(world.Server.IsObserver(entity, peerId));
        }

        [Test]
        public void APeriodicRuleIsAskedAgainByTheRound()
        {
            var world = new ClientEntitiesTest.World();
            ulong peerId = world.ClientObjects.LocalPeerId;
            var rule = new CountingRule(true) { Observes = false };
            world.Server.ObserverRule = rule;
            ClientEntitiesTest.FakeEntity entity = world.Served();
            world.Server.Spawn(entity, 0);
            rule.Observes = true;

            for (int tick = 0; tick < 15; tick++)
            {
                world.Server.RebuildObserversRound();
            }

            Assert.IsTrue(world.Server.IsObserver(entity, peerId));
        }

        [Test]
        public void WithoutARuleTheRoundLeavesAVisibilityChangeToARebuild()
        {
            var world = new ClientEntitiesTest.World();
            ulong peerId = world.ClientObjects.LocalPeerId;
            ClientEntitiesTest.FakeEntity entity = world.Served();
            entity.Visibility = NetworkVisibility.OwnerOnly;
            world.Server.Spawn(entity, 0);
            entity.Visibility = NetworkVisibility.Everyone;

            for (int tick = 0; tick < 30; tick++)
            {
                world.Server.RebuildObserversRound();
            }

            Assert.IsFalse(world.Server.IsObserver(entity, peerId));

            world.Server.RebuildObservers(entity);

            Assert.IsTrue(world.Server.IsObserver(entity, peerId));
        }

        [Test]
        public void APeerThatDisconnectsLeavesItsGroups()
        {
            var world = new ClientEntitiesTest.World();
            ulong peerId = world.ClientObjects.LocalPeerId;
            world.Server.Groups.Add(Room, peerId);

            world.ServerSession.Disconnect(peerId);

            Assert.IsFalse(world.Server.Groups.Contains(Room, peerId));
        }

        [Test]
        public void DespawningForgetsTheGroupsOfTheEntity()
        {
            var world = new ClientEntitiesTest.World();
            ClientEntitiesTest.FakeEntity entity = world.Served();
            world.Server.Groups.Add(Room, entity);
            world.Server.Spawn(entity, 0);

            world.Server.Despawn(entity);

            Assert.IsFalse(world.Server.Groups.Contains(Room, entity));
        }

        private sealed class CountingRule : IObserverRule
        {
            public CountingRule(bool periodic)
            {
                RebuildsPeriodically = periodic;
            }

            public bool Observes { get; set; } = true;

            public int Calls { get; private set; }

            public bool RebuildsOnFirstAnchor => false;

            public bool RebuildsPeriodically { get; }

            bool IObserverRule.Observes(ObserverContext context, INetworkEntity entity, ulong peerId)
            {
                Calls++;
                return Observes;
            }
        }
    }
}
