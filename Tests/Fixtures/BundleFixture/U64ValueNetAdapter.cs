using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class U64ValueNetAdapter : IMessageCodec<U64Value>
    {
        public static readonly U64ValueNetAdapter Instance = new U64ValueNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => U64ValueNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(U64Value value)
        {
            writer.Clear();
            U64ValueNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref U64Value value)
        {
            var reader = new Reader(payload);
            try
            {
                U64ValueNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
