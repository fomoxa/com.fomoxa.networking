using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class U16ValueNetAdapter : IMessageCodec<U16Value>
    {
        public static readonly U16ValueNetAdapter Instance = new U16ValueNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => U16ValueNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(U16Value value)
        {
            writer.Clear();
            U16ValueNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref U16Value value)
        {
            var reader = new Reader(payload);
            try
            {
                U16ValueNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
