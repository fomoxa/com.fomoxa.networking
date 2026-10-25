using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class CollectionContentNetAdapter : IMessageCodec<CollectionContent>
    {
        public static readonly CollectionContentNetAdapter Instance = new CollectionContentNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => CollectionContentNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(CollectionContent value)
        {
            writer.Clear();
            CollectionContentNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref CollectionContent value)
        {
            var reader = new Reader(payload);
            try
            {
                CollectionContentNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
