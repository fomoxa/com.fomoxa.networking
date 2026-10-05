using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Fomoxa.Networking.Messaging;
using UnityEngine;

namespace Fomoxa.Unity.Tests.Support
{
    public sealed class MoveState
    {
        public int Position { get; set; }
    }

    public sealed class MoveStateCodec : IMessageCodec<MoveState>
    {
        public static readonly MoveStateCodec Instance = new MoveStateCodec();

        private readonly byte[] buffer = new byte[4];

        public uint MessageId => 0x2000_0042;

        public ReadOnlyMemory<byte> Encode(MoveState value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value.Position);
            return buffer;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref MoveState value)
        {
            if (payload.Length < 4)
            {
                throw new MessageDecodeException($"a move state needs 4 bytes, got {payload.Length}", null);
            }

            value.Position = BinaryPrimitives.ReadInt32LittleEndian(payload.Span);
        }
    }

    public class ReconcileBehaviour : NetworkBehaviour
    {
        public bool MoveTransform;

        public readonly Dictionary<uint, int> PositionAt = new Dictionary<uint, int>();

        public readonly List<uint> ReconciledTicks = new List<uint>();

        public int Position { get; private set; }

        public int Bias { get; set; }

        public int ReplayedApplies { get; private set; }

        protected virtual bool UsesMatches => false;

        protected override void OnRegisterInput(NetworkInput input)
        {
            input.Use(MoveInputCodec.Instance, Gather, Apply);
            input.Reconcile(MoveStateCodec.Instance, Capture, Restore, UsesMatches ? (Func<MoveState, MoveState, bool>)CloseEnough : null);
        }

        protected override void OnReconciled(uint tick)
        {
            ReconciledTicks.Add(tick);
        }

        private static bool CloseEnough(MoveState server, MoveState predicted) => Math.Abs(server.Position - predicted.Position) <= 1000;

        private void Gather(MoveInput input)
        {
            input.Step = 1;
        }

        private void Apply(MoveInput input, InputContext context)
        {
            Position += (int)input.Step + Bias;
            Bias = 0;
            if (context.Replaying)
            {
                ReplayedApplies++;
            }

            PositionAt[context.Tick] = Position;
            Place();
        }

        private void Capture(MoveState state)
        {
            state.Position = Position;
        }

        private void Restore(MoveState state)
        {
            Position = state.Position;
            Place();
        }

        private void Place()
        {
            if (MoveTransform)
            {
                transform.position = new Vector3(Position, 0f, 0f);
            }
        }
    }

    public sealed class LenientReconcileBehaviour : ReconcileBehaviour
    {
        protected override bool UsesMatches => true;
    }

    public sealed class EarlyReconcileBehaviour : NetworkBehaviour
    {
        protected override void OnRegisterInput(NetworkInput input)
        {
            input.Reconcile(MoveStateCodec.Instance, state => { }, state => { });
        }
    }

    public sealed class TwoReconcileBehaviour : NetworkBehaviour
    {
        protected override void OnRegisterInput(NetworkInput input)
        {
            input.Use(MoveInputCodec.Instance, value => { }, (value, context) => { });
            input.Reconcile(MoveStateCodec.Instance, state => { }, state => { });
            input.Reconcile(MoveStateCodec.Instance, state => { }, state => { });
        }
    }
}
