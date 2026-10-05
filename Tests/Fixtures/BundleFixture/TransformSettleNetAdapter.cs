using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class TransformSettleNetAdapter : IMessageCodec<TransformSettle>
    {
        public static readonly TransformSettleNetAdapter Instance = new TransformSettleNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => TransformSettleNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(TransformSettle value)
        {
            writer.Clear();
            TransformSettleNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref TransformSettle value)
        {
            var reader = new Reader(payload);
            try
            {
                TransformSettleNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
