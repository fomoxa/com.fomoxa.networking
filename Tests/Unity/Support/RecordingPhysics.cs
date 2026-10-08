using System;
using System.Collections.Generic;
using Fomoxa.Networking.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity.Tests.Support
{
    public sealed class RecordingPhysics : NetworkPhysics
    {
        public readonly List<string> Calls = new List<string>();

        public PhysicsBackend Code { get; set; } = PhysicsBackend.Rapier;

        public override PhysicsBackend Backend => Code;

        public override void Begin() => Calls.Add("begin");

        public override void Release() => Calls.Add("release");

        public override void WorldsOf(Scene scene, List<IPhysicsSimulation> worlds)
        {
        }

        public override PhysicsBody BodyOf(NetworkObject networkObject) => default;

        public override PhysicsBody2D Body2DOf(NetworkObject networkObject) => default;

        public override PhysicsHistory HistoryOf(IPhysicsSimulation world, int capacity) => new PhysicsHistory(world, capacity);

        public override void PlaceProxy(NetworkObject networkObject)
        {
        }

        public override void EndProxy(NetworkObject networkObject)
        {
        }

        public override void ForgetDestroyedProxies()
        {
        }

        public override void WorldsToStep(List<IPhysicsSimulation> worlds) => Calls.Add("step");

        public override IContactTracker TrackerOf(IPhysicsSimulation world) => null;

        public override StaticGroup AddStatic(Scene scene, IReadOnlyList<ColliderDesc> colliders) => throw new NotSupportedException();

        public override StaticGroup AddStatic2D(Scene scene, IReadOnlyList<ColliderDesc2D> colliders) => throw new NotSupportedException();

        public override StaticGroup AddStatic(GameObject root) => throw new NotSupportedException();

        public override StaticGroup AddStatic2D(GameObject root) => throw new NotSupportedException();

        public override void RemoveStatic(StaticGroup group) => throw new NotSupportedException();

        public override PhysicsBody AddBody(Scene scene, in BodyDesc body) => throw new NotSupportedException();

        public override PhysicsBody2D AddBody2D(Scene scene, in BodyDesc2D body) => throw new NotSupportedException();

        public override void RemoveBody(PhysicsBody body) => throw new NotSupportedException();

        public override void RemoveBody2D(PhysicsBody2D body) => throw new NotSupportedException();
    }
}
