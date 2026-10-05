using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class PeerLeaveNetAdapter : IMessageCodec<PeerLeave>
    {
        public static readonly PeerLeaveNetAdapter Instance = new PeerLeaveNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => PeerLeaveNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(PeerLeave value)
        {
            writer.Clear();
            PeerLeaveNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref PeerLeave value)
        {
            var reader = new Reader(payload);
            try
            {
                PeerLeaveNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
