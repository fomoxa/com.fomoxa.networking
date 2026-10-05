using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class StateResyncNetAdapter : IMessageCodec<StateResync>
    {
        public static readonly StateResyncNetAdapter Instance = new StateResyncNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => StateResyncNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(StateResync value)
        {
            writer.Clear();
            StateResyncNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref StateResync value)
        {
            var reader = new Reader(payload);
            try
            {
                StateResyncNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
