using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class SceneUnloadNetAdapter : IMessageCodec<SceneUnload>
    {
        public static readonly SceneUnloadNetAdapter Instance = new SceneUnloadNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => SceneUnloadNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(SceneUnload value)
        {
            writer.Clear();
            SceneUnloadNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref SceneUnload value)
        {
            var reader = new Reader(payload);
            try
            {
                SceneUnloadNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
