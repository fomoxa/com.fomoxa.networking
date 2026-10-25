using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class U32ValueNetAdapter : IMessageCodec<U32Value>
    {
        public static readonly U32ValueNetAdapter Instance = new U32ValueNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => U32ValueNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(U32Value value)
        {
            writer.Clear();
            U32ValueNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref U32Value value)
        {
            var reader = new Reader(payload);
            try
            {
                U32ValueNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
