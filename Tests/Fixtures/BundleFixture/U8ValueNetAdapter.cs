using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class U8ValueNetAdapter : IMessageCodec<U8Value>
    {
        public static readonly U8ValueNetAdapter Instance = new U8ValueNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => U8ValueNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(U8Value value)
        {
            writer.Clear();
            U8ValueNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref U8Value value)
        {
            var reader = new Reader(payload);
            try
            {
                U8ValueNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
