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

        public abstract StaticGroup AddStatic(Scene scene, IReadOnlyList<ColliderDesc> colliders);

        public abstract StaticGroup AddStatic2D(Scene scene, IReadOnlyList<ColliderDesc2D> colliders);

        public abstract StaticGroup AddStatic(GameObject root);

        public abstract StaticGroup AddStatic2D(GameObject root);

        public abstract void RemoveStatic(StaticGroup group);

        public abstract PhysicsBody AddBody(Scene scene, in BodyDesc body);

        public abstract PhysicsBody2D AddBody2D(Scene scene, in BodyDesc2D body);

        public abstract void RemoveBody(PhysicsBody body);

        public abstract void RemoveBody2D(PhysicsBody2D body);
    }
}
