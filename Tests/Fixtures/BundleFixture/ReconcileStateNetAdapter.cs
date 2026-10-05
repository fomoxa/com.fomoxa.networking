using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class ReconcileStateNetAdapter : IMessageCodec<ReconcileState>
    {
        public static readonly ReconcileStateNetAdapter Instance = new ReconcileStateNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => ReconcileStateNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(ReconcileState value)
        {
            writer.Clear();
            ReconcileStateNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref ReconcileState value)
        {
            var reader = new Reader(payload);
            try
            {
                ReconcileStateNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
