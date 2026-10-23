using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class FragmentStartNetAdapter : IMessageCodec<FragmentStart>
    {
        public static readonly FragmentStartNetAdapter Instance = new FragmentStartNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => FragmentStartNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(FragmentStart value)
        {
            writer.Clear();
            FragmentStartNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref FragmentStart value)
        {
            var reader = new Reader(payload);
            try
            {
                FragmentStartNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
