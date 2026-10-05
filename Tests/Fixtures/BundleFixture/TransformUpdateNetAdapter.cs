using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class TransformUpdateNetAdapter : IMessageCodec<TransformUpdate>
    {
        public static readonly TransformUpdateNetAdapter Instance = new TransformUpdateNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => TransformUpdateNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(TransformUpdate value)
        {
            writer.Clear();
            TransformUpdateNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref TransformUpdate value)
        {
            var reader = new Reader(payload);
            try
            {
                TransformUpdateNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
