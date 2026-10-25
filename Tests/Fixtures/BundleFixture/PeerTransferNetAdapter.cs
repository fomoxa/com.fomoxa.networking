using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class PeerTransferNetAdapter : IMessageCodec<PeerTransfer>
    {
        public static readonly PeerTransferNetAdapter Instance = new PeerTransferNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => PeerTransferNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(PeerTransfer value)
        {
            writer.Clear();
            PeerTransferNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref PeerTransfer value)
        {
            var reader = new Reader(payload);
            try
            {
                PeerTransferNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
