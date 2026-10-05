using System;
using System.Buffers.Binary;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Unity.Tests.Support
{
    public sealed class CounterState
    {
        public uint Value { get; set; }

        public uint Spare { get; set; }
    }

    public sealed class CounterStateCodec : IMessageCodec<CounterState>
    {
        private readonly byte[] buffer = new byte[8];
        private readonly bool empty;

        public CounterStateCodec(uint messageId, bool empty = false)
        {
            MessageId = messageId;
            this.empty = empty;
        }

        public uint MessageId { get; }

        public ReadOnlyMemory<byte> Encode(CounterState value)
        {
            if (empty)
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value.Value);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), value.Spare);
            return buffer;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref CounterState value)
        {
            if (payload.Length == 0)
            {
                value.Value = 0;
                value.Spare = 0;
                return;
            }

            if (payload.Length < 8)
            {
                throw new MessageDecodeException($"a counter state needs 8 bytes, got {payload.Length}", null);
            }

            value.Value = BinaryPrimitives.ReadUInt32LittleEndian(payload.Span);
            value.Spare = BinaryPrimitives.ReadUInt32LittleEndian(payload.Span.Slice(4));
        }
    }

    public static class StateCodecs
    {
        public const uint CounterId = 0x2000_0031;
        public const uint UnreliableId = 0x2000_0032;
        public const uint EmptyId = 0x2000_0033;

        public static readonly CounterStateCodec Counter = new CounterStateCodec(CounterId);
        public static readonly CounterStateCodec Unreliable = new CounterStateCodec(UnreliableId);
        public static readonly CounterStateCodec Empty = new CounterStateCodec(EmptyId, empty: true);
    }
}
