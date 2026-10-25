using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class F64ValueNetAdapter : IMessageCodec<F64Value>
    {
        public static readonly F64ValueNetAdapter Instance = new F64ValueNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => F64ValueNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(F64Value value)
        {
            writer.Clear();
            F64ValueNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref F64Value value)
        {
            var reader = new Reader(payload);
            try
            {
                F64ValueNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
