using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Unity.Tests.Support
{
    public sealed class WideState
    {
        public List<uint> Values { get; set; } = new List<uint>(new uint[16]);
    }

    public sealed class WideStateCodec : IMessageCodec<WideState>
    {
        public const uint WideId = 0x2000_0034;

        public static readonly WideStateCodec Instance = new WideStateCodec();

        private byte[] buffer = new byte[0];

        public uint MessageId => WideId;

        public ReadOnlyMemory<byte> Encode(WideState value)
        {
            int length = 4 + (4 * value.Values.Count);
            if (buffer.Length < length)
            {
                buffer = new byte[length];
            }

            BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)value.Values.Count);
            for (int index = 0; index < value.Values.Count; index++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4 + (4 * index)), value.Values[index]);
            }

            return new ReadOnlyMemory<byte>(buffer, 0, length);
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref WideState value)
        {
            ReadOnlySpan<byte> span = payload.Span;
            if (span.Length == 0)
            {
                value.Values.Clear();
                return;
            }

            if (span.Length < 4 || span.Length != 4 + (4 * (long)BinaryPrimitives.ReadUInt32LittleEndian(span)))
            {
                throw new MessageDecodeException($"a wide state of {span.Length} bytes does not match its count", null);
            }

            int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(span);
            value.Values.Clear();
            for (int index = 0; index < count; index++)
            {
                value.Values.Add(BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4 + (4 * index))));
            }
        }
    }
}
