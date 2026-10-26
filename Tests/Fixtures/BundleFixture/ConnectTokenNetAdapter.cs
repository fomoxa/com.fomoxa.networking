using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class ConnectTokenNetAdapter : IMessageCodec<ConnectToken>
    {
        public static readonly ConnectTokenNetAdapter Instance = new ConnectTokenNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => ConnectTokenNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(ConnectToken value)
        {
            writer.Clear();
            ConnectTokenNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref ConnectToken value)
        {
            var reader = new Reader(payload);
            try
            {
                ConnectTokenNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
