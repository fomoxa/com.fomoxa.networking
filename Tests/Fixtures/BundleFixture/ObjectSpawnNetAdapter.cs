using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class ObjectSpawnNetAdapter : IMessageCodec<ObjectSpawn>
    {
        public static readonly ObjectSpawnNetAdapter Instance = new ObjectSpawnNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => ObjectSpawnNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(ObjectSpawn value)
        {
            writer.Clear();
            ObjectSpawnNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref ObjectSpawn value)
        {
            var reader = new Reader(payload);
            try
            {
                ObjectSpawnNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
