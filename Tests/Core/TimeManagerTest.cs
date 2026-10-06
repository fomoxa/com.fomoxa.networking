using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class TimeManagerTest
    {
        [Test]
        public void ElapsedTimeBecomesDueTicksAndKeepsTheRemainder()
        {
            var time = new TimeManager(30, 3, TimingMode.Tick);

            Assert.AreEqual(0, time.Advance(1.0 / 60));
            Assert.AreEqual(1, time.Advance(1.0 / 60));
            Assert.AreEqual(2, time.Advance(2.0 / 30));
        }

        [Test]
        public void AFrameRunsAtMostTheConfiguredTicksAndDropsTheRest()
        {
            var time = new TimeManager(30, 3, TimingMode.Tick);

            Assert.AreEqual(3, time.Advance(1.0));
            Assert.AreEqual(0, time.Advance(0.0));
        }

        [Test]
        public void EachTickRaisesPreTickTickAndPostTickInOrder()
        {
            var time = new TimeManager(30, 3, TimingMode.Tick);
            var calls = new List<string>();
            time.OnPreTick += () => calls.Add($"pre {time.Tick}");
            time.OnTick += () => calls.Add($"tick {time.Tick}");
            time.OnPostTick += () => calls.Add($"post {time.Tick}");

            time.RaisePreTick(TimeSpan.Zero);
            time.RaiseTick();
            time.RaisePostTick();

            CollectionAssert.AreEqual(new[] { "pre 1", "tick 1", "post 1" }, calls);
        }

        [Test]
        public void AFollowedTickRateLastsUntilRestored()
        {
            var time = new TimeManager(30, 3, TimingMode.Tick);

            time.UseTickRate(60);

            Assert.AreEqual(60, time.TickRate);
            Assert.AreEqual(1.0 / 60, time.TickDelta);

            time.RestoreTickRate();

            Assert.AreEqual(30, time.TickRate);
            Assert.Throws<ArgumentOutOfRangeException>(() => time.UseTickRate(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TimeManager(30, 0, TimingMode.Tick));
        }
    }
}
