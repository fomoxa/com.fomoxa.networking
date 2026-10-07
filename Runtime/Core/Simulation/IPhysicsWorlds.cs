using System.Collections.Generic;

namespace Fomoxa.Networking.Simulation
{
    public interface IPhysicsWorlds
    {
        PhysicsBackend Backend { get; }

        void WorldsToStep(List<IPhysicsSimulation> worlds);

        IContactTracker TrackerOf(IPhysicsSimulation world);
    }
}
