using System;
using System.Collections.Generic;
using System.Numerics;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Unity.Tests.Support
{
    public sealed class PhysicsReconcileBehaviour2D : NetworkBehaviour
    {
        public const float Push = 0.1f;
        public const float Tolerance = 1e-3f;

        public readonly Dictionary<uint, float> PositionAt = new Dictionary<uint, float>();

        public readonly List<uint> ReconciledTicks = new List<uint>();

        private uint appliedTick;

        public float Bias { get; set; }

        public int ReplayedApplies { get; private set; }

        public int ReplayFlagMismatches { get; private set; }

        protected override void OnRegisterInput(NetworkInput input)
        {
            input.Use(MoveInputCodec.Instance, Gather, Apply);
            input.Reconcile(BodyMotionCodec.Instance, Capture, Restore, CloseEnough);
        }

        protected override void OnReconciled(uint tick)
        {
            ReconciledTicks.Add(tick);
        }

        private static bool CloseEnough(BodyMotion server, BodyMotion predicted) =>
            Math.Abs(server.X - predicted.X) <= Tolerance && Math.Abs(server.VelocityX - predicted.VelocityX) <= Tolerance;

        private void Gather(MoveInput input)
        {
            input.Step = 1;
        }

        private void Apply(MoveInput input, InputContext context)
        {
            NetworkObject.Body2D.AddImpulse(new Vector2(input.Step * Push + Bias, 0f));
            Bias = 0f;
            appliedTick = context.Tick;
            if (context.Replaying)
            {
                ReplayedApplies++;
            }

            if (context.Replaying != IsReplaying)
            {
                ReplayFlagMismatches++;
            }
        }

        private void Capture(BodyMotion state)
        {
            PhysicsBody2D body = NetworkObject.Body2D;
            state.X = body.Position.X;
            state.VelocityX = body.Velocity.X;
            PositionAt[appliedTick] = state.X;
        }

        private void Restore(BodyMotion state)
        {
            PhysicsBody2D body = NetworkObject.Body2D;
            BodyState2D current = body.State;
            current.Position = new Vector2(state.X, current.Position.Y);
            current.Velocity = new Vector2(state.VelocityX, 0f);
            body.State = current;
        }
    }
}
