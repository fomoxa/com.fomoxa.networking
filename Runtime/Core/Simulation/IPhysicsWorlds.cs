using System.Collections.Generic;

namespace Fomoxa.Networking.Simulation
{
    internal interface IPhysicsWorlds
    {
        void WorldsToStep(List<IPhysicsSimulation> worlds);

        IContactTracker TrackerOf(IPhysicsSimulation world);
    }
}
