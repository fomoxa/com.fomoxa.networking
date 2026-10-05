using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class StateDeltaNetAdapter : IMessageCodec<StateDelta>
    {
        public static readonly StateDeltaNetAdapter Instance = new StateDeltaNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => StateDeltaNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(StateDelta value)
        {
            writer.Clear();
            StateDeltaNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref StateDelta value)
        {
            var reader = new Reader(payload);
            try
            {
                StateDeltaNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
