using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class BoolValueNetAdapter : IMessageCodec<BoolValue>
    {
        public static readonly BoolValueNetAdapter Instance = new BoolValueNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => BoolValueNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(BoolValue value)
        {
            writer.Clear();
            BoolValueNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref BoolValue value)
        {
            var reader = new Reader(payload);
            try
            {
                BoolValueNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
