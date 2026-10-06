using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class SceneFileNetAdapter : IMessageCodec<SceneFile>
    {
        public static readonly SceneFileNetAdapter Instance = new SceneFileNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => SceneFileNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(SceneFile value)
        {
            writer.Clear();
            SceneFileNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref SceneFile value)
        {
            var reader = new Reader(payload);
            try
            {
                SceneFileNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
