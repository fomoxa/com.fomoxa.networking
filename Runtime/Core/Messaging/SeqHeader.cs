using System;
using System.Buffers.Binary;

namespace Fomoxa.Networking.Messaging
{
    public static class SeqHeader
    {
        public const int Length = 2;

        public static void Write(Span<byte> destination, ushort seq) => BinaryPrimitives.WriteUInt16LittleEndian(destination, seq);

        public static ushort Read(ReadOnlySpan<byte> header) => BinaryPrimitives.ReadUInt16LittleEndian(header);
    }
}
