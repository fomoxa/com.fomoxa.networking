using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class ObjectSceneSpawnNetAdapter : IMessageCodec<ObjectSceneSpawn>
    {
        public static readonly ObjectSceneSpawnNetAdapter Instance = new ObjectSceneSpawnNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => ObjectSceneSpawnNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(ObjectSceneSpawn value)
        {
            writer.Clear();
            ObjectSceneSpawnNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref ObjectSceneSpawn value)
        {
            var reader = new Reader(payload);
            try
            {
                ObjectSceneSpawnNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
