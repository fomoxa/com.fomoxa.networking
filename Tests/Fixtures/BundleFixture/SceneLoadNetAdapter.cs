using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class SceneLoadNetAdapter : IMessageCodec<SceneLoad>
    {
        public static readonly SceneLoadNetAdapter Instance = new SceneLoadNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => SceneLoadNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(SceneLoad value)
        {
            writer.Clear();
            SceneLoadNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref SceneLoad value)
        {
            var reader = new Reader(payload);
            try
            {
                SceneLoadNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
