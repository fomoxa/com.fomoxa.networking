using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class TransferTicketNetAdapter : IMessageCodec<TransferTicket>
    {
        public static readonly TransferTicketNetAdapter Instance = new TransferTicketNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => TransferTicketNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(TransferTicket value)
        {
            writer.Clear();
            TransferTicketNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref TransferTicket value)
        {
            var reader = new Reader(payload);
            try
            {
                TransferTicketNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
