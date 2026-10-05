using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using Fomoxa.Networking;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Unity.Tests.Support
{
    public sealed class BodyMotion
    {
        public float X { get; set; }

        public float VelocityX { get; set; }
    }

    public sealed class BodyMotionCodec : IMessageCodec<BodyMotion>
    {
        public static readonly BodyMotionCodec Instance = new BodyMotionCodec();

        private readonly byte[] buffer = new byte[8];

        public uint MessageId => 0x2000_0043;

        public ReadOnlyMemory<byte> Encode(BodyMotion value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer, BitConverter.SingleToInt32Bits(value.X));
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), BitConverter.SingleToInt32Bits(value.VelocityX));
            return buffer;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref BodyMotion value)
        {
            if (payload.Length < 8)
            {
                throw new MessageDecodeException($"a body motion needs 8 bytes, got {payload.Length}", null);
            }

            value.X = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(payload.Span));
            value.VelocityX = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(payload.Span.Slice(4)));
        }
    }

    public sealed class PhysicsReconcileBehaviour : NetworkBehaviour
    {
        public const float Push = 0.1f;
        public const float Tolerance = 1e-3f;

        public readonly Dictionary<uint, float> PositionAt = new Dictionary<uint, float>();

        public readonly List<uint> ReconciledTicks = new List<uint>();

        private uint appliedTick;

        public float Bias { get; set; }

        public int ReplayedApplies { get; private set; }

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
            NetworkObject.Body.AddImpulse(new Vector3(input.Step * Push + Bias, 0f, 0f));
            Bias = 0f;
            appliedTick = context.Tick;
            if (context.Replaying)
            {
                ReplayedApplies++;
            }
        }

        private void Capture(BodyMotion state)
        {
            PhysicsBody body = NetworkObject.Body;
            state.X = body.Position.X;
            state.VelocityX = body.Velocity.X;
            PositionAt[appliedTick] = state.X;
        }

        private void Restore(BodyMotion state)
        {
            PhysicsBody body = NetworkObject.Body;
            BodyState current = body.State;
            current.Position = new Vector3(state.X, current.Position.Y, current.Position.Z);
            current.Velocity = new Vector3(state.VelocityX, 0f, 0f);
            body.State = current;
        }
    }
}
