using System;

namespace Fomoxa.Networking.Messaging
{
    public enum OutgoingKind
    {
        Queued,
        Ack,
        Retransmit,
    }

    public readonly struct OutgoingEntry
    {
        private OutgoingEntry(OutgoingKind kind, uint messageId, ReadOnlyMemory<byte> data, int inFlightIndex)
        {
            Kind = kind;
            MessageId = messageId;
            Data = data;
            InFlightIndex = inFlightIndex;
        }

        public OutgoingKind Kind { get; }

        public uint MessageId { get; }

        public ReadOnlyMemory<byte> Data { get; }

        public int InFlightIndex { get; }

        public static OutgoingEntry Queued(uint messageId, byte[] data) => new OutgoingEntry(OutgoingKind.Queued, messageId, data, -1);

        public static OutgoingEntry Ack(uint messageId, ReadOnlyMemory<byte> data) => new OutgoingEntry(OutgoingKind.Ack, messageId, data, -1);

        public static OutgoingEntry Retransmit(uint messageId, byte[] data, int inFlightIndex) =>
            new OutgoingEntry(OutgoingKind.Retransmit, messageId, data, inFlightIndex);
    }
}
