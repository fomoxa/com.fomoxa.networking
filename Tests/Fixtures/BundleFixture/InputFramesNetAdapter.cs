using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class InputFramesNetAdapter : IMessageCodec<InputFrames>
    {
        public static readonly InputFramesNetAdapter Instance = new InputFramesNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => InputFramesNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(InputFrames value)
        {
            writer.Clear();
            InputFramesNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref InputFrames value)
        {
            var reader = new Reader(payload);
            try
            {
                InputFramesNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
