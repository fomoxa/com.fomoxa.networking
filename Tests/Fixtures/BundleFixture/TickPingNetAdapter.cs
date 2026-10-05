using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class TickPingNetAdapter : IMessageCodec<TickPing>
    {
        public static readonly TickPingNetAdapter Instance = new TickPingNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => TickPingNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(TickPing value)
        {
            writer.Clear();
            TickPingNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref TickPing value)
        {
            var reader = new Reader(payload);
            try
            {
                TickPingNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
