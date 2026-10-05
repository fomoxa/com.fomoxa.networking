using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class AnimatorStateNetAdapter : IMessageCodec<AnimatorState>
    {
        public static readonly AnimatorStateNetAdapter Instance = new AnimatorStateNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => AnimatorStateNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(AnimatorState value)
        {
            writer.Clear();
            AnimatorStateNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref AnimatorState value)
        {
            var reader = new Reader(payload);
            try
            {
                AnimatorStateNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
