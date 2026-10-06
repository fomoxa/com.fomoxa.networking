using System.Numerics;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class DistanceObserverRuleTest
    {
        [Test]
        public void APeerObservesWhatIsWithinTheRadiusOfAnEntityItOwns()
        {
            var world = new ClientEntitiesTest.World();
            ulong peerId = world.ClientObjects.LocalPeerId;
            world.Server.ObserverRule = new DistanceObserverRule(5);
            ClientEntitiesTest.FakeEntity anchor = world.Served();
            ClientEntitiesTest.FakeEntity near = At(world, new Vector3(3, 0, 0));
            ClientEntitiesTest.FakeEntity far = At(world, new Vector3(10, 0, 0));

            world.Server.Spawn(anchor, peerId);
            world.Server.Spawn(near, 0);
            world.Server.Spawn(far, 0);

            Assert.IsTrue(world.Server.IsObserver(anchor, peerId));
            Assert.IsTrue(world.Server.IsObserver(near, peerId));
            Assert.IsFalse(world.Server.IsObserver(far, peerId));
        }

        [Test]
        public void ARadiusPerEntityOverridesTheRadiusOfTheRule()
        {
            var world = new ClientEntitiesTest.World();
            ulong peerId = world.ClientObjects.LocalPeerId;
            ClientEntitiesTest.FakeEntity far = At(world, new Vector3(10, 0, 0));
            world.Server.ObserverRule = new WideFor(far);

            world.Server.Spawn(world.Served(), peerId);
            world.Server.Spawn(far, 0);

            Assert.IsTrue(world.Server.IsObserver(far, peerId));
        }

        [Test]
        public void TheFirstAnchorOfAPeerRebuildsWhatItObserves()
        {
            var world = new ClientEntitiesTest.World();
            ulong peerId = world.ClientObjects.LocalPeerId;
            world.Server.ObserverRule = new DistanceObserverRule(5);
            ClientEntitiesTest.FakeEntity near = At(world, new Vector3(3, 0, 0));
            world.Server.Spawn(near, 0);
            Assert.IsFalse(world.Server.IsObserver(near, peerId));

            world.Server.Spawn(world.Served(), peerId);

            Assert.IsTrue(world.Server.IsObserver(near, peerId));
        }

        [Test]
        public void APeerWithoutAnchorsObservesNothingThroughTheRule()
        {
            var world = new ClientEntitiesTest.World();
            var rule = new DistanceObserverRule(1000);
            ClientEntitiesTest.FakeEntity entity = world.Served();
            world.Server.Spawn(entity, 0);

            Assert.IsFalse(rule.Observes(world.Server.Observers, entity, world.ClientObjects.LocalPeerId));
            Assert.AreEqual(0f, new DistanceObserverRule(-1).Radius);
        }

        private static ClientEntitiesTest.FakeEntity At(ClientEntitiesTest.World world, Vector3 position)
        {
            ClientEntitiesTest.FakeEntity entity = world.Served();
            entity.Position = position;
            return entity;
        }

        private sealed class WideFor : DistanceObserverRule
        {
            private readonly INetworkEntity wide;

            public WideFor(INetworkEntity wide)
                : base(5)
            {
                this.wide = wide;
            }

            protected override float RadiusOf(INetworkEntity entity) => entity == wide ? 20 : Radius;
        }
    }
}
