using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class ObjectOwnerChangeNetAdapter : IMessageCodec<ObjectOwnerChange>
    {
        public static readonly ObjectOwnerChangeNetAdapter Instance = new ObjectOwnerChangeNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => ObjectOwnerChangeNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(ObjectOwnerChange value)
        {
            writer.Clear();
            ObjectOwnerChangeNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref ObjectOwnerChange value)
        {
            var reader = new Reader(payload);
            try
            {
                ObjectOwnerChangeNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
