using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class I64ValueNetAdapter : IMessageCodec<I64Value>
    {
        public static readonly I64ValueNetAdapter Instance = new I64ValueNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => I64ValueNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(I64Value value)
        {
            writer.Clear();
            I64ValueNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref I64Value value)
        {
            var reader = new Reader(payload);
            try
            {
                I64ValueNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
