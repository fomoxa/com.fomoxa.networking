using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class CollectionChangesNetAdapter : IMessageCodec<CollectionChanges>
    {
        public static readonly CollectionChangesNetAdapter Instance = new CollectionChangesNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => CollectionChangesNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(CollectionChanges value)
        {
            writer.Clear();
            CollectionChangesNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref CollectionChanges value)
        {
            var reader = new Reader(payload);
            try
            {
                CollectionChangesNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
