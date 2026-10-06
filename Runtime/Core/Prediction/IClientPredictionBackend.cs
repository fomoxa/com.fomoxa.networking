using System.Collections.Generic;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Prediction
{
    internal interface IClientPredictionBackend
    {
        void WorldsOf(INetworkEntity entity, List<IPhysicsSimulation> worlds);

        PhysicsHistory HistoryOf(IPhysicsSimulation world, int capacity);

        void PlaceProxy(INetworkEntity entity);

        void EndProxy(INetworkEntity entity);

        void ForgetDestroyedProxies();

        void BeginCorrection(INetworkEntity entity);

        void EndCorrection(INetworkEntity entity);

        void RestoreContacts(IPhysicsSimulation world, uint tick);

        void QueryContacts(IPhysicsSimulation world, uint tick, bool record, int capacity);

        void PublishContacts(IPhysicsSimulation world);
    }
}
