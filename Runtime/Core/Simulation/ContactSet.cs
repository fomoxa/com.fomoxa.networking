using System;
using System.Collections.Generic;

namespace Fomoxa.Networking.Simulation
{
    public sealed class ContactSet<TContact>
    {
        private readonly HashSet<TContact> published = new HashSet<TContact>();
        private readonly HashSet<TContact> current = new HashSet<TContact>();
        private readonly List<TContact> exits = new List<TContact>();
        private readonly List<TContact> enters = new List<TContact>();
        private HashSet<TContact>[] history = Array.Empty<HashSet<TContact>>();
        private uint[] historyTicks = Array.Empty<uint>();
        private bool[] historyFilled = Array.Empty<bool>();

        public IReadOnlyCollection<TContact> Published => published;

        public void Query(IContactSource<TContact> source)
        {
            current.Clear();
            source.Collect(current);
        }

        public void Record(uint tick, int capacity)
        {
            if (history.Length != capacity)
            {
                history = new HashSet<TContact>[capacity];
                historyTicks = new uint[capacity];
                historyFilled = new bool[capacity];
            }

            int slot = (int)(tick % (uint)capacity);
            HashSet<TContact> kept = history[slot] ??= new HashSet<TContact>();
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

        public void Publish(IContactSource<TContact> source)
        {
            exits.Clear();
            enters.Clear();
            foreach (TContact other in published)
            {
                if (!current.Contains(other))
                {
                    exits.Add(other);
                }
            }

            foreach (TContact other in current)
            {
                if (!published.Contains(other))
                {
                    enters.Add(other);
                }
            }

            published.Clear();
            published.UnionWith(current);
            foreach (TContact other in exits)
            {
                source.RaiseExit(other);
            }

            foreach (TContact other in enters)
            {
                source.RaiseEnter(other);
            }

            exits.Clear();
            enters.Clear();
        }

        public void PassEnter(IContactSource<TContact> source, TContact other)
        {
            if (published.Add(other))
            {
                current.Add(other);
                source.RaiseEnter(other);
            }
        }

        public void PassExit(IContactSource<TContact> source, TContact other)
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
}
