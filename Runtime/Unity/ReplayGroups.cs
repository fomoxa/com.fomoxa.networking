using System.Collections.Generic;
using UnityEngine;

namespace Fomoxa.Unity
{
    internal sealed class ReplayGroup
    {
        public readonly List<UnityPhysicsWorld> Worlds = new List<UnityPhysicsWorld>();
        public readonly List<UnityPhysicsWorld2D> Worlds2D = new List<UnityPhysicsWorld2D>();

        public bool Holds(UnityPhysicsWorld world, UnityPhysicsWorld2D world2D) => Worlds.Contains(world) || Worlds2D.Contains(world2D);

        public void Include(UnityPhysicsWorld world, UnityPhysicsWorld2D world2D)
        {
            if (!Worlds.Contains(world))
            {
                Worlds.Add(world);
            }

            if (!Worlds2D.Contains(world2D))
            {
                Worlds2D.Add(world2D);
            }
        }

        public void Absorb(ReplayGroup other)
        {
            foreach (UnityPhysicsWorld world in other.Worlds)
            {
                if (!Worlds.Contains(world))
                {
                    Worlds.Add(world);
                }
            }

            foreach (UnityPhysicsWorld2D world in other.Worlds2D)
            {
                if (!Worlds2D.Contains(world))
                {
                    Worlds2D.Add(world);
                }
            }
        }

        public void RestoreContacts(uint tick)
        {
            foreach (UnityPhysicsWorld world in Worlds)
            {
                ContactTrackers.Of(world.PhysicsScene)?.Restore(tick);
            }

            foreach (UnityPhysicsWorld2D world in Worlds2D)
            {
                ContactTrackers.Of(world.PhysicsScene)?.Restore(tick);
            }
        }

        public void QueryContacts(uint tick, bool record, int capacity)
        {
            foreach (UnityPhysicsWorld world in Worlds)
            {
                ContactTracker<Collider> tracker = ContactTrackers.Of(world.PhysicsScene);
                tracker?.Query();
                if (record)
                {
                    tracker?.Record(tick, capacity);
                }
            }

            foreach (UnityPhysicsWorld2D world in Worlds2D)
            {
                ContactTracker<Collider2D> tracker = ContactTrackers.Of(world.PhysicsScene);
                tracker?.Query();
                if (record)
                {
                    tracker?.Record(tick, capacity);
                }
            }
        }

        public void PublishContacts()
        {
            foreach (UnityPhysicsWorld world in Worlds)
            {
                ContactTrackers.Of(world.PhysicsScene)?.Publish();
            }

            foreach (UnityPhysicsWorld2D world in Worlds2D)
            {
                ContactTrackers.Of(world.PhysicsScene)?.Publish();
            }
        }

        public void Clear()
        {
            Worlds.Clear();
            Worlds2D.Clear();
        }
    }

    internal sealed class ReplayGroups
    {
        private readonly List<ReplayGroup> groups = new List<ReplayGroup>();
        private readonly Stack<ReplayGroup> spare = new Stack<ReplayGroup>();

        public IReadOnlyList<ReplayGroup> Groups => groups;

        public void Add(UnityPhysicsWorld world, UnityPhysicsWorld2D world2D)
        {
            ReplayGroup joined = null;
            for (int index = groups.Count - 1; index >= 0; index--)
            {
                ReplayGroup group = groups[index];
                if (!group.Holds(world, world2D))
                {
                    continue;
                }

                if (joined == null)
                {
                    joined = group;
                    continue;
                }

                joined.Absorb(group);
                Recycle(group);
                groups.RemoveAt(index);
            }

            if (joined == null)
            {
                joined = spare.Count > 0 ? spare.Pop() : new ReplayGroup();
                groups.Add(joined);
            }

            joined.Include(world, world2D);
        }

        public void Clear()
        {
            foreach (ReplayGroup group in groups)
            {
                Recycle(group);
            }

            groups.Clear();
        }

        private void Recycle(ReplayGroup group)
        {
            group.Clear();
            spare.Push(group);
        }
    }
}
