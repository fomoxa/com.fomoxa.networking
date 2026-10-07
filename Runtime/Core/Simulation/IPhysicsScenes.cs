using System.Collections.Generic;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Networking.Simulation
{
    public interface IPhysicsScenes : IPhysicsWorlds
    {
        void LoadScene(SceneFile file);

        void UnloadScene(uint sceneId);

        PhysicsBody AddBody(INetworkEntity entity, uint sceneId, in BodyDesc body);

        PhysicsBody2D AddBody2D(INetworkEntity entity, uint sceneId, in BodyDesc2D body);

        void RemoveBodies(INetworkEntity entity);

        void WorldsOf(INetworkEntity entity, List<IPhysicsSimulation> worlds);

        PhysicsHistory HistoryOf(IPhysicsSimulation world, int capacity);

        void PlaceProxy(INetworkEntity entity);

        void EndProxy(INetworkEntity entity);
    }
}
