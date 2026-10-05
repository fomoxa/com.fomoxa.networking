using System;
using System.Buffers.Binary;

namespace Fomoxa.Networking.Messaging
{
    public static class ObjectHeader
    {
        public const int UnreliableLength = 5;

        public static void WriteUnreliable(Span<byte> destination, uint objectId, byte behaviourIndex)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination, objectId);
            destination[4] = behaviourIndex;
        }

        public static uint ReadObjectId(ReadOnlySpan<byte> header) => BinaryPrimitives.ReadUInt32LittleEndian(header);

        public static byte ReadBehaviourIndex(ReadOnlySpan<byte> header) => header[4];
    }
}
