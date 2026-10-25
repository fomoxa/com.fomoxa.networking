using System;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public sealed class SessionCloseNetAdapter : IMessageCodec<SessionClose>
    {
        public static readonly SessionCloseNetAdapter Instance = new SessionCloseNetAdapter();

        private readonly Writer writer = new Writer();

        public uint MessageId => SessionCloseNetCodec.MessageId;

        public ReadOnlyMemory<byte> Encode(SessionClose value)
        {
            writer.Clear();
            SessionCloseNetCodec.Encode(writer, value);
            return writer.WrittenMemory;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref SessionClose value)
        {
            var reader = new Reader(payload);
            try
            {
                SessionCloseNetCodec.Decode(ref reader, ref value);
            }
            catch (DecodeException error)
            {
                throw new MessageDecodeException(error.Message, error);
            }
        }
    }
}
