using System;
using System.Collections.Generic;

namespace Fomoxa.Networking.Simulation
{
    public sealed class PhysicsHistories
    {
        private readonly Dictionary<IPhysicsSimulation, PhysicsHistory> histories = new Dictionary<IPhysicsSimulation, PhysicsHistory>();

        public PhysicsHistory Of(IPhysicsSimulation world, int capacity)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (!histories.TryGetValue(world, out PhysicsHistory history) || history.Capacity != capacity)
            {
                history = new PhysicsHistory(world, capacity);
                histories[world] = history;
            }

            return history;
        }

        public void Forget(IPhysicsSimulation world) => histories.Remove(world);
    }
}
