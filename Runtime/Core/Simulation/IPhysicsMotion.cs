using System.Collections.Generic;

namespace Fomoxa.Networking.Simulation
{
    public interface IPhysicsMotion
    {
        bool ReportsMotion(INetworkEntity entity);

        void CollectMoved(List<INetworkEntity> into);
    }
}
