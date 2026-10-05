using System;

namespace Fomoxa.Networking.Simulation
{
    public sealed class PhysicsHistory
    {
        private readonly IPhysicsSimulation world;
        private readonly PhysicsSnapshot[] snapshots;
        private readonly uint[] ticks;
        private readonly bool[] filled;

        public PhysicsHistory(IPhysicsSimulation world, int capacity)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "the physics history keeps at least one tick");
            }

            snapshots = new PhysicsSnapshot[capacity];
            ticks = new uint[capacity];
            filled = new bool[capacity];
        }

        public int Capacity => snapshots.Length;

        public void Save(uint tick)
        {
            int slot = Slot(tick);
            if (snapshots[slot] == null)
            {
                snapshots[slot] = world.CreateSnapshot();
            }

            world.Save(snapshots[slot]);
            ticks[slot] = tick;
            filled[slot] = true;
        }

        public bool Has(uint tick)
        {
            int slot = Slot(tick);
            return filled[slot] && ticks[slot] == tick;
        }

        public bool Load(uint tick)
        {
            if (!Has(tick))
            {
                return false;
            }

            world.Load(snapshots[Slot(tick)]);
            return true;
        }

        public void Clear() => Array.Clear(filled, 0, filled.Length);

        private int Slot(uint tick) => (int)(tick % (uint)snapshots.Length);
    }
}
