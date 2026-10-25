using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class CollectionResyncNetAdapter : IMessageCodec<CollectionResync>
    {
        public static readonly CollectionResyncNetAdapter Instance = new CollectionResyncNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => CollectionResyncNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(CollectionResync value)
        {
            writer.Clear();
            CollectionResyncNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref CollectionResync value)
        {
            var reader = new Reader(payload);
            try
            {
                CollectionResyncNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
