using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Unity.Tests.Support
{
    public sealed class MoveInput
    {
        public uint Step { get; set; }
    }

    public sealed class MoveInputCodec : IMessageCodec<MoveInput>
    {
        public static readonly MoveInputCodec Instance = new MoveInputCodec();

        private readonly byte[] buffer = new byte[4];

        public uint MessageId => 0x2000_0041;

        public ReadOnlyMemory<byte> Encode(MoveInput value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value.Step);
            return buffer;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref MoveInput value)
        {
            if (payload.Length < 4)
            {
                throw new MessageDecodeException($"a move input needs 4 bytes, got {payload.Length}", null);
            }

            value.Step = BinaryPrimitives.ReadUInt32LittleEndian(payload.Span);
        }
    }

    public readonly struct AppliedInput
    {
        public AppliedInput(uint tick, uint step, bool repeated, uint repeatedTicks = 0)
        {
            Tick = tick;
            Step = step;
            Repeated = repeated;
            RepeatedTicks = repeatedTicks;
        }

        public uint Tick { get; }

        public uint Step { get; }

        public bool Repeated { get; }

        public uint RepeatedTicks { get; }
    }

    public class InputBehaviour : NetworkBehaviour
    {
        public const uint ThrowingStep = 0xDEAD;

        public readonly List<AppliedInput> Applied = new List<AppliedInput>();

        public uint Tag { get; set; }

        public uint NextStep { get; set; }

        public int Gathered { get; private set; }

        protected override void OnRegisterInput(NetworkInput input)
        {
            input.Use(MoveInputCodec.Instance, Gather, Apply);
        }

        private void Gather(MoveInput input)
        {
            Gathered++;
            NextStep++;
            input.Step = Tag + NextStep;
        }

        private void Apply(MoveInput input, InputContext context)
        {
            if (input.Step == ThrowingStep)
            {
                throw new InvalidOperationException("apply failed");
            }

            Applied.Add(new AppliedInput(context.Tick, input.Step, context.Repeated, context.RepeatedTicks));
        }
    }

    public sealed class TwoInputBehaviour : NetworkBehaviour
    {
        protected override void OnRegisterInput(NetworkInput input)
        {
            input.Use(MoveInputCodec.Instance, value => { }, (value, context) => { });
            input.Use(MoveInputCodec.Instance, value => { }, (value, context) => { });
        }
    }
}
