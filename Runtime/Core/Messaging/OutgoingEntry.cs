using System;

namespace Fomoxa.Networking.Messaging
{
    public enum OutgoingKind
    {
        Queued,
        Ack,
        Retransmit,
        Fragment,
    }

    public readonly struct OutgoingEntry
    {
        private OutgoingEntry(OutgoingKind kind, uint messageId, ReadOnlyMemory<byte> data, int inFlightIndex, int splitEnd)
        {
            Kind = kind;
            MessageId = messageId;
            Data = data;
            InFlightIndex = inFlightIndex;
            SplitEnd = splitEnd;
        }

        public OutgoingKind Kind { get; }

        public uint MessageId { get; }

        public ReadOnlyMemory<byte> Data { get; }

        public int InFlightIndex { get; }

        public int SplitEnd { get; }

        public static OutgoingEntry Queued(uint messageId, byte[] data) => new OutgoingEntry(OutgoingKind.Queued, messageId, data, -1, 0);

        public static OutgoingEntry Ack(uint messageId, ReadOnlyMemory<byte> data) => new OutgoingEntry(OutgoingKind.Ack, messageId, data, -1, 0);

        public static OutgoingEntry Retransmit(uint messageId, ReadOnlyMemory<byte> data, int inFlightIndex) =>
            new OutgoingEntry(OutgoingKind.Retransmit, messageId, data, inFlightIndex, 0);

        public static OutgoingEntry Fragment(uint messageId, byte[] data, int splitEnd) =>
            new OutgoingEntry(OutgoingKind.Fragment, messageId, data, -1, splitEnd);
    }
}
