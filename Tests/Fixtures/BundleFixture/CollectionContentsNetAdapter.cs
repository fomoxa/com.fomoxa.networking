using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class CollectionContentsNetAdapter : IMessageCodec<CollectionContents>
    {
        public static readonly CollectionContentsNetAdapter Instance = new CollectionContentsNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => CollectionContentsNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(CollectionContents value)
        {
            writer.Clear();
            CollectionContentsNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref CollectionContents value)
        {
            var reader = new Reader(payload);
            try
            {
                CollectionContentsNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
