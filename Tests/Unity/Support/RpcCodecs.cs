using System;
using System.Buffers.Binary;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Unity.Tests.Support
{
    public sealed class RpcValue
    {
        public uint Value { get; set; }
    }

    public sealed class RpcValueCodec : IMessageCodec<RpcValue>
    {
        private readonly byte[] buffer = new byte[4];

        public RpcValueCodec(uint messageId)
        {
            MessageId = messageId;
        }

        public uint MessageId { get; }

        public ReadOnlyMemory<byte> Encode(RpcValue value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value.Value);
            return buffer;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref RpcValue value)
        {
            if (payload.Length < 4)
            {
                throw new MessageDecodeException($"an RPC value needs 4 bytes, got {payload.Length}", null);
            }

            value.Value = BinaryPrimitives.ReadUInt32LittleEndian(payload.Span);
        }
    }

    public static class RpcCodecs
    {
        public const uint FireId = 0x2000_0011;
        public const uint OpenId = 0x2000_0012;
        public const uint AnnounceId = 0x2000_0013;
        public const uint WhisperId = 0x2000_0014;
        public const uint UndeclaredId = 0x2000_0015;
        public const uint ThrowingValue = 0xDEAD;

        public static readonly RpcValueCodec Fire = new RpcValueCodec(FireId);
        public static readonly RpcValueCodec Open = new RpcValueCodec(OpenId);
        public static readonly RpcValueCodec Announce = new RpcValueCodec(AnnounceId);
        public static readonly RpcValueCodec Whisper = new RpcValueCodec(WhisperId);
        public static readonly RpcValueCodec Undeclared = new RpcValueCodec(UndeclaredId);
    }
}
