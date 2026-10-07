using System.Collections.Generic;
using Fomoxa.Networking.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity
{
    public abstract class NetworkPhysics : MonoBehaviour, IPhysicsWorlds
    {
        public abstract PhysicsBackend Backend { get; }

        public virtual bool Simulates => true;

        public abstract void Begin();

        public abstract void Release();

        public abstract void WorldsOf(Scene scene, List<IPhysicsSimulation> worlds);

        public abstract PhysicsBody BodyOf(NetworkObject networkObject);

        public abstract PhysicsBody2D Body2DOf(NetworkObject networkObject);

        public abstract PhysicsHistory HistoryOf(IPhysicsSimulation world, int capacity);

        public abstract void PlaceProxy(NetworkObject networkObject);

        public abstract void EndProxy(NetworkObject networkObject);

        public abstract void ForgetDestroyedProxies();

        public abstract void WorldsToStep(List<IPhysicsSimulation> worlds);

        public abstract IContactTracker TrackerOf(IPhysicsSimulation world);
    }
}
