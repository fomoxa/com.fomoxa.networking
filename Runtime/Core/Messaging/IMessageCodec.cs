using System;

namespace Fomoxa.Networking.Messaging
{
    public interface IMessageCodec<T>
    {
        uint MessageId { get; }

        ReadOnlyMemory<byte> Encode(T value);

        void Decode(ReadOnlyMemory<byte> payload, ref T value);
    }
}
