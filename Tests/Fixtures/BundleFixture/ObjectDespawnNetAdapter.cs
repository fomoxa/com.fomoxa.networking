using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class ObjectDespawnNetAdapter : IMessageCodec<ObjectDespawn>
    {
        public static readonly ObjectDespawnNetAdapter Instance = new ObjectDespawnNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => ObjectDespawnNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(ObjectDespawn value)
        {
            writer.Clear();
            ObjectDespawnNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref ObjectDespawn value)
        {
            var reader = new Reader(payload);
            try
            {
                ObjectDespawnNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
