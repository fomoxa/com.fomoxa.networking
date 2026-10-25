using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class I8ValueNetAdapter : IMessageCodec<I8Value>
    {
        public static readonly I8ValueNetAdapter Instance = new I8ValueNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => I8ValueNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(I8Value value)
        {
            writer.Clear();
            I8ValueNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref I8Value value)
        {
            var reader = new Reader(payload);
            try
            {
                I8ValueNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
