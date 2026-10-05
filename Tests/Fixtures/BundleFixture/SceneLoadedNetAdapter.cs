using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class SceneLoadedNetAdapter : IMessageCodec<SceneLoaded>
    {
        public static readonly SceneLoadedNetAdapter Instance = new SceneLoadedNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => SceneLoadedNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(SceneLoaded value)
        {
            writer.Clear();
            SceneLoadedNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref SceneLoaded value)
        {
            var reader = new Reader(payload);
            try
            {
                SceneLoadedNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
