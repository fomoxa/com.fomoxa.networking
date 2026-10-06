using System.Collections.Generic;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Prediction
{
    internal sealed class ReplayGroup
    {
        public readonly List<IPhysicsSimulation> Worlds = new List<IPhysicsSimulation>();

        public bool HoldsAny(List<IPhysicsSimulation> worlds)
        {
            foreach (IPhysicsSimulation world in worlds)
            {
                if (Worlds.Contains(world))
                {
                    return true;
                }
            }

            return false;
        }

        public bool HoldsAny(List<IPhysicsSimulation> worlds, int start, int count)
        {
            for (int index = start; index < start + count; index++)
            {
                if (Worlds.Contains(worlds[index]))
                {
                    return true;
                }
            }

            return false;
        }

        public void Include(List<IPhysicsSimulation> worlds)
        {
            foreach (IPhysicsSimulation world in worlds)
            {
                if (!Worlds.Contains(world))
                {
                    Worlds.Add(world);
                }
            }
        }

        public void Clear() => Worlds.Clear();
    }

    internal sealed class ReplayGroups
    {
        private readonly List<ReplayGroup> groups = new List<ReplayGroup>();
        private readonly Stack<ReplayGroup> spare = new Stack<ReplayGroup>();

        public List<ReplayGroup> Groups => groups;

        public void Add(List<IPhysicsSimulation> worlds)
        {
            ReplayGroup joined = null;
            for (int index = groups.Count - 1; index >= 0; index--)
            {
                ReplayGroup group = groups[index];
                if (!group.HoldsAny(worlds))
                {
                    continue;
                }

                if (joined == null)
                {
                    joined = group;
                    continue;
                }

                joined.Include(group.Worlds);
                Recycle(group);
                groups.RemoveAt(index);
            }

            if (joined == null)
            {
                joined = spare.Count > 0 ? spare.Pop() : new ReplayGroup();
                groups.Add(joined);
            }

            joined.Include(worlds);
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
