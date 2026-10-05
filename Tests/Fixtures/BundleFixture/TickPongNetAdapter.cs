using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class TickPongNetAdapter : IMessageCodec<TickPong>
    {
        public static readonly TickPongNetAdapter Instance = new TickPongNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => TickPongNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(TickPong value)
        {
            writer.Clear();
            TickPongNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref TickPong value)
        {
            var reader = new Reader(payload);
            try
            {
                TickPongNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
