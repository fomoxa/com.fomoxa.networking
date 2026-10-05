using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class MessageBundleNetAdapter : IMessageCodec<MessageBundle>
    {
        public static readonly MessageBundleNetAdapter Instance = new MessageBundleNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => MessageBundleNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(MessageBundle value)
        {
            writer.Clear();
            MessageBundleNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref MessageBundle value)
        {
            var reader = new Reader(payload);
            try
            {
                MessageBundleNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
