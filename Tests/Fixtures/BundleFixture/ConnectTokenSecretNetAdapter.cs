using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class ConnectTokenSecretNetAdapter : IMessageCodec<ConnectTokenSecret>
    {
        public static readonly ConnectTokenSecretNetAdapter Instance = new ConnectTokenSecretNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => ConnectTokenSecretNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(ConnectTokenSecret value)
        {
            writer.Clear();
            ConnectTokenSecretNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref ConnectTokenSecret value)
        {
            var reader = new Reader(payload);
            try
            {
                ConnectTokenSecretNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
