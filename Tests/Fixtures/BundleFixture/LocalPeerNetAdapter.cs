using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class LocalPeerNetAdapter : IMessageCodec<LocalPeer>
    {
        public static readonly LocalPeerNetAdapter Instance = new LocalPeerNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => LocalPeerNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(LocalPeer value)
        {
            writer.Clear();
            LocalPeerNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref LocalPeer value)
        {
            var reader = new Reader(payload);
            try
            {
                LocalPeerNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
