using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class I16ValueNetAdapter : IMessageCodec<I16Value>
    {
        public static readonly I16ValueNetAdapter Instance = new I16ValueNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => I16ValueNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(I16Value value)
        {
            writer.Clear();
            I16ValueNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref I16Value value)
        {
            var reader = new Reader(payload);
            try
            {
                I16ValueNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
