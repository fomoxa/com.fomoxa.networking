using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class ConnectionGrantNetAdapter : IMessageCodec<ConnectionGrant>
    {
        public static readonly ConnectionGrantNetAdapter Instance = new ConnectionGrantNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => ConnectionGrantNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(ConnectionGrant value)
        {
            writer.Clear();
            ConnectionGrantNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref ConnectionGrant value)
        {
            var reader = new Reader(payload);
            try
            {
                ConnectionGrantNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
