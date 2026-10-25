using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class F32ValueNetAdapter : IMessageCodec<F32Value>
    {
        public static readonly F32ValueNetAdapter Instance = new F32ValueNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => F32ValueNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(F32Value value)
        {
            writer.Clear();
            F32ValueNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref F32Value value)
        {
            var reader = new Reader(payload);
            try
            {
                F32ValueNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
