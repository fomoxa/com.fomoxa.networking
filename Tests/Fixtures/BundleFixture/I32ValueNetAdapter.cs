using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class I32ValueNetAdapter : IMessageCodec<I32Value>
    {
        public static readonly I32ValueNetAdapter Instance = new I32ValueNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => I32ValueNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(I32Value value)
        {
            writer.Clear();
            I32ValueNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref I32Value value)
        {
            var reader = new Reader(payload);
            try
            {
                I32ValueNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
