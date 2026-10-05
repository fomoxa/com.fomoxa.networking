using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fomoxa.Unity
{
    internal interface IContactSource<TCollider>
        where TCollider : Component
    {
        ContactSet<TCollider> Contacts { get; }

        bool MovedWorld { get; }

        void Collect(HashSet<TCollider> into);

        void Rejoin();

        void RaiseEnter(TCollider other);

        void RaiseExit(TCollider other);
    }

    internal sealed class ContactSet<TCollider>
        where TCollider : Component
    {
        private readonly HashSet<TCollider> published = new HashSet<TCollider>();
        private readonly HashSet<TCollider> current = new HashSet<TCollider>();
        private readonly List<TCollider> exits = new List<TCollider>();
        private readonly List<TCollider> enters = new List<TCollider>();
        private HashSet<TCollider>[] history = Array.Empty<HashSet<TCollider>>();
        private uint[] historyTicks = Array.Empty<uint>();
        private bool[] historyFilled = Array.Empty<bool>();

        public IReadOnlyCollection<TCollider> Published => published;

        public void Query(IContactSource<TCollider> source)
        {
            current.Clear();
            source.Collect(current);
        }

        public void Record(uint tick, int capacity)
        {
            if (history.Length != capacity)
            {
                history = new HashSet<TCollider>[capacity];
                historyTicks = new uint[capacity];
                historyFilled = new bool[capacity];
            }

            int slot = (int)(tick % (uint)capacity);
            HashSet<TCollider> kept = history[slot] ??= new HashSet<TCollider>();
            kept.Clear();
            kept.UnionWith(current);
            historyTicks[slot] = tick;
            historyFilled[slot] = true;
        }

        public void Restore(uint tick)
        {
            if (history.Length == 0)
            {
                return;
            }

            int slot = (int)(tick % (uint)history.Length);
            if (!historyFilled[slot] || historyTicks[slot] != tick)
            {
                return;
            }

            current.Clear();
            current.UnionWith(history[slot]);
        }

        public void Publish(IContactSource<TCollider> source)
        {
            exits.Clear();
            enters.Clear();
            foreach (TCollider other in published)
            {
                if (!current.Contains(other))
                {
                    exits.Add(other);
                }
            }

            foreach (TCollider other in current)
            {
                if (!published.Contains(other))
                {
                    enters.Add(other);
                }
            }

            published.Clear();
            published.UnionWith(current);
            foreach (TCollider other in exits)
            {
                source.RaiseExit(other);
            }

            foreach (TCollider other in enters)
            {
                source.RaiseEnter(other);
            }

            exits.Clear();
            enters.Clear();
        }

        public void PassEnter(IContactSource<TCollider> source, TCollider other)
        {
            if (published.Add(other))
            {
                current.Add(other);
                source.RaiseEnter(other);
            }
        }

        public void PassExit(IContactSource<TCollider> source, TCollider other)
        {
            if (published.Remove(other))
            {
                current.Remove(other);
                source.RaiseExit(other);
            }
        }

        public void Clear()
        {
            published.Clear();
            current.Clear();
            Array.Clear(historyFilled, 0, historyFilled.Length);
        }
    }

    internal sealed class ContactTracker<TCollider>
        where TCollider : Component
    {
        private readonly List<IContactSource<TCollider>> sources = new List<IContactSource<TCollider>>();
        private readonly List<IContactSource<TCollider>> visiting = new List<IContactSource<TCollider>>();

        public int Count => sources.Count;

        public void Add(IContactSource<TCollider> source)
        {
            if (!sources.Contains(source))
            {
                sources.Add(source);
            }
        }

        public void Remove(IContactSource<TCollider> source) => sources.Remove(source);

        public void Query()
        {
            visiting.Clear();
            visiting.AddRange(sources);
            foreach (IContactSource<TCollider> source in visiting)
            {
                if (source.MovedWorld)
                {
                    source.Rejoin();
                }
                else
                {
                    source.Contacts.Query(source);
                }
            }

            visiting.Clear();
        }

        public void Record(uint tick, int capacity)
        {
            foreach (IContactSource<TCollider> source in sources)
            {
                source.Contacts.Record(tick, capacity);
            }
        }

        public void Restore(uint tick)
        {
            foreach (IContactSource<TCollider> source in sources)
            {
                source.Contacts.Restore(tick);
            }
        }

        public void Publish()
        {
            visiting.Clear();
            visiting.AddRange(sources);
            foreach (IContactSource<TCollider> source in visiting)
            {
                source.Contacts.Publish(source);
            }

            visiting.Clear();
        }
    }

    internal static class ContactTrackers
    {
        private static readonly Dictionary<PhysicsScene, ContactTracker<Collider>> trackers = new Dictionary<PhysicsScene, ContactTracker<Collider>>();
        private static readonly Dictionary<PhysicsScene2D, ContactTracker<Collider2D>> trackers2D = new Dictionary<PhysicsScene2D, ContactTracker<Collider2D>>();

        public static int Count => trackers.Count + trackers2D.Count;

        public static ContactTracker<Collider> Of(PhysicsScene physicsScene) =>
            trackers.TryGetValue(physicsScene, out ContactTracker<Collider> tracker) ? tracker : null;

        public static ContactTracker<Collider2D> Of(PhysicsScene2D physicsScene) =>
            trackers2D.TryGetValue(physicsScene, out ContactTracker<Collider2D> tracker) ? tracker : null;

        public static void Join(PhysicsScene physicsScene, IContactSource<Collider> source)
        {
            if (!trackers.TryGetValue(physicsScene, out ContactTracker<Collider> tracker))
            {
                tracker = new ContactTracker<Collider>();
                trackers.Add(physicsScene, tracker);
            }

            tracker.Add(source);
        }

        public static void Join(PhysicsScene2D physicsScene, IContactSource<Collider2D> source)
        {
            if (!trackers2D.TryGetValue(physicsScene, out ContactTracker<Collider2D> tracker))
            {
                tracker = new ContactTracker<Collider2D>();
                trackers2D.Add(physicsScene, tracker);
            }

            tracker.Add(source);
        }

        public static void Leave(PhysicsScene physicsScene, IContactSource<Collider> source)
        {
            if (trackers.TryGetValue(physicsScene, out ContactTracker<Collider> tracker))
            {
                tracker.Remove(source);
                if (tracker.Count == 0)
                {
                    trackers.Remove(physicsScene);
                }
            }
        }

        public static void Leave(PhysicsScene2D physicsScene, IContactSource<Collider2D> source)
        {
            if (trackers2D.TryGetValue(physicsScene, out ContactTracker<Collider2D> tracker))
            {
                tracker.Remove(source);
                if (tracker.Count == 0)
                {
                    trackers2D.Remove(physicsScene);
                }
            }
        }
    }
}
