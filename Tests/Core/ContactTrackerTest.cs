using System.Collections.Generic;
using Fomoxa.Networking.Simulation;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class ContactTrackerTest
    {
        [Test]
        public void PublishRaisesExitsBeforeEnters()
        {
            var worlds = new Worlds();
            Source source = worlds.Add(1);
            source.Touching.UnionWith(new[] { 1, 2 });
            worlds[1].Query();
            worlds[1].Publish();
            source.Events.Clear();

            source.Touching.Clear();
            source.Touching.UnionWith(new[] { 2, 3 });
            worlds[1].Query();
            worlds[1].Publish();

            CollectionAssert.AreEqual(new[] { "exit 1", "enter 3" }, source.Events);
            CollectionAssert.AreEquivalent(new[] { 2, 3 }, source.Contacts.Published);
        }

        [Test]
        public void RestoringATickPublishesTheDifferenceFromItsRecordedSet()
        {
            var worlds = new Worlds();
            Source source = worlds.Add(1);
            source.Touching.Add(1);
            worlds[1].Query();
            worlds[1].Record(10, 8);
            worlds[1].Publish();
            source.Touching.Clear();
            source.Touching.Add(2);
            worlds[1].Query();
            worlds[1].Record(11, 8);
            source.Events.Clear();

            worlds[1].Restore(10);
            worlds[1].Publish();

            CollectionAssert.IsEmpty(source.Events);

            worlds[1].Restore(11);
            worlds[1].Publish();

            CollectionAssert.AreEqual(new[] { "exit 1", "enter 2" }, source.Events);
        }

        [Test]
        public void PassedCallbacksRaiseOnceAndKeepTheSetsInStep()
        {
            var worlds = new Worlds();
            Source source = worlds.Add(1);

            source.Contacts.PassEnter(source, 5);
            source.Contacts.PassEnter(source, 5);
            source.Contacts.PassExit(source, 5);
            source.Contacts.PassExit(source, 5);

            CollectionAssert.AreEqual(new[] { "enter 5", "exit 5" }, source.Events);
            CollectionAssert.IsEmpty(source.Contacts.Published);
        }

        [Test]
        public void ASourceThatMovedWorldRejoinsAndLaterExitsWhatItNoLongerTouches()
        {
            var worlds = new Worlds();
            Source source = worlds.Add(1);
            source.Touching.Add(1);
            worlds[1].Query();
            worlds[1].Publish();
            source.Events.Clear();
            source.Touching.Clear();
            ContactTracker<int> second = worlds[2];
            int collects = source.Collects;

            source.World = 2;
            worlds[1].Query();

            Assert.AreEqual(0, worlds[1].Count);
            Assert.AreEqual(1, second.Count);
            Assert.IsFalse(((IContactSource<int>)source).MovedWorld);
            Assert.AreEqual(collects, source.Collects);

            second.Query();
            second.Publish();

            Assert.AreEqual(collects + 1, source.Collects);
            CollectionAssert.AreEqual(new[] { "exit 1" }, source.Events);
        }

        private sealed class Worlds
        {
            private readonly Dictionary<int, ContactTracker<int>> trackers = new Dictionary<int, ContactTracker<int>>();

            public ContactTracker<int> this[int world]
            {
                get
                {
                    if (!trackers.TryGetValue(world, out ContactTracker<int> tracker))
                    {
                        tracker = new ContactTracker<int>();
                        trackers.Add(world, tracker);
                    }

                    return tracker;
                }
            }

            public Source Add(int world)
            {
                var source = new Source(this) { World = world };
                source.Join();
                return source;
            }
        }

        private sealed class Source : IContactSource<int>
        {
            private readonly Worlds worlds;
            private int joined;

            public Source(Worlds worlds)
            {
                this.worlds = worlds;
            }

            public int World { get; set; }

            public HashSet<int> Touching { get; } = new HashSet<int>();

            public List<string> Events { get; } = new List<string>();

            public int Collects { get; private set; }

            public ContactSet<int> Contacts { get; } = new ContactSet<int>();

            bool IContactSource<int>.MovedWorld => World != joined;

            public void Join()
            {
                joined = World;
                worlds[joined].Add(this);
            }

            void IContactSource<int>.Collect(HashSet<int> into)
            {
                Collects++;
                into.UnionWith(Touching);
            }

            void IContactSource<int>.Rejoin()
            {
                worlds[joined].Remove(this);
                Join();
            }

            void IContactSource<int>.RaiseEnter(int other) => Events.Add($"enter {other}");

            void IContactSource<int>.RaiseExit(int other) => Events.Add($"exit {other}");
        }
    }
}
