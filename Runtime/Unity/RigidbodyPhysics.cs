using System;
using System.Collections.Generic;
using Fomoxa.Networking.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity
{
    public sealed class RigidbodyPhysics : NetworkPhysics
    {
        [SerializeField] private bool simulatePhysics;

        private readonly PhysicsWorlds worlds = new PhysicsWorlds();
        private bool begun;

        public override PhysicsBackend Backend => PhysicsBackend.Rigidbody;

        public override bool Simulates => simulatePhysics;

        internal PhysicsWorlds Worlds => worlds;

        public override void Begin()
        {
            if (begun)
            {
                throw new InvalidOperationException("this RigidbodyPhysics is already simulating for another NetworkManager");
            }

            begun = true;
            PhysicsSimulationOwner.Acquire();
        }

        public override void Release()
        {
            if (!begun)
            {
                return;
            }

            begun = false;
            worlds.ReleaseStepping();
            PhysicsSimulationOwner.Release();
        }

        public override void WorldsOf(Scene scene, List<IPhysicsSimulation> into)
        {
            into.Add(worlds.Of(scene));
            into.Add(worlds.Of2D(scene));
        }

        public override PhysicsBody BodyOf(NetworkObject networkObject) => worlds.BodyOf(networkObject);

        public override PhysicsBody2D Body2DOf(NetworkObject networkObject) => worlds.Body2DOf(networkObject);

        public override PhysicsHistory HistoryOf(IPhysicsSimulation world, int capacity) => worlds.HistoryOf(world, capacity);

        public override void PlaceProxy(NetworkObject networkObject) => worlds.PlaceProxy(networkObject);

        public override void EndProxy(NetworkObject networkObject) => worlds.EndProxy(networkObject);

        public override void ForgetDestroyedProxies() => worlds.ForgetDestroyedProxies();

        public override void WorldsToStep(List<IPhysicsSimulation> into) => worlds.WorldsToStep(into);

        public override IContactTracker TrackerOf(IPhysicsSimulation world) => worlds.TrackerOf(world);
    }
}
