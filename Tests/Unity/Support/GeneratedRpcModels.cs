using System.Buffers.Binary;
using System;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking;

namespace Fomoxa.Unity.Tests.Support
{
    [Fomoxa.Network]
    [Fomoxa.Codec("net")]
    [Fomoxa.Codec("wide")]
    public sealed class GeneratedValue
    {
        public uint Value { get; set; }
    }

    [Fomoxa.Network]
    [Fomoxa.Codec("net")]
    public sealed class GeneratedNotice
    {
        public uint Value { get; set; }
    }

    public abstract class GeneratedAdapter<T> : IMessageCodec<T>
        where T : class
    {
        private readonly byte[] buffer = new byte[4];
        private readonly Func<T, uint> read;
        private readonly Action<T, uint> write;

        protected GeneratedAdapter(uint messageId, Func<T, uint> read, Action<T, uint> write)
        {
            MessageId = messageId;
            this.read = read;
            this.write = write;
        }

        public uint MessageId { get; }

        public ReadOnlyMemory<byte> Encode(T value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, read(value));
            return buffer;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref T value)
        {
            if (payload.Length < 4)
            {
                throw new MessageDecodeException($"a generated RPC value needs 4 bytes, got {payload.Length}", null);
            }

            write(value, BinaryPrimitives.ReadUInt32LittleEndian(payload.Span));
        }
    }

    [MessageAdapter("net")]
    public sealed class GeneratedValueNetAdapter : GeneratedAdapter<GeneratedValue>
    {
        public const uint Id = 0x2000_0031;

        public static readonly GeneratedValueNetAdapter Instance = new GeneratedValueNetAdapter();

        private GeneratedValueNetAdapter()
            : base(Id, value => value.Value, (value, read) => value.Value = read)
        {
        }
    }

    [MessageAdapter("wide")]
    public sealed class GeneratedValueWideAdapter : GeneratedAdapter<GeneratedValue>
    {
        public const uint Id = 0x2000_0032;

        public static readonly GeneratedValueWideAdapter Instance = new GeneratedValueWideAdapter();

        private GeneratedValueWideAdapter()
            : base(Id, value => value.Value, (value, read) => value.Value = read)
        {
        }
    }

    [MessageAdapter("net")]
    public sealed class GeneratedNoticeNetAdapter : GeneratedAdapter<GeneratedNotice>
    {
        public const uint Id = 0x2000_0033;

        public static readonly GeneratedNoticeNetAdapter Instance = new GeneratedNoticeNetAdapter();

        private GeneratedNoticeNetAdapter()
            : base(Id, notice => notice.Value, (notice, read) => notice.Value = read)
        {
        }
    }
}
