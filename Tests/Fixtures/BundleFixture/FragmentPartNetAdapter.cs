using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class FragmentPartNetAdapter : IMessageCodec<FragmentPart>
    {
        public static readonly FragmentPartNetAdapter Instance = new FragmentPartNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => FragmentPartNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(FragmentPart value)
        {
            writer.Clear();
            FragmentPartNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref FragmentPart value)
        {
            var reader = new Reader(payload);
            try
            {
                FragmentPartNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
