using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class ReliableAckNetAdapter : IMessageCodec<ReliableAck>
    {
        public static readonly ReliableAckNetAdapter Instance = new ReliableAckNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => ReliableAckNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(ReliableAck value)
        {
            writer.Clear();
            ReliableAckNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref ReliableAck value)
        {
            var reader = new Reader(payload);
            try
            {
                ReliableAckNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
