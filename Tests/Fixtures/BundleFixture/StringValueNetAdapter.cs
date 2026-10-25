using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class StringValueNetAdapter : IMessageCodec<StringValue>
    {
        public static readonly StringValueNetAdapter Instance = new StringValueNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => StringValueNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(StringValue value)
        {
            writer.Clear();
            StringValueNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref StringValue value)
        {
            var reader = new Reader(payload);
            try
            {
                StringValueNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
