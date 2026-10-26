using System;
using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    public class ConnectToken
    {
        [Network("u8")]
        [Codec("net")]
        public byte Version { get; set; }

        [Network("u64")]
        [Codec("net")]
        public ulong SessionId { get; set; }

        [Network("i64")]
        [Codec("net")]
        public long Expires { get; set; }

        [Network("bytes")]
        [Codec("net")]
        public ReadOnlyMemory<byte> Nonce { get; set; }

        [Network("bytes")]
        [Codec("net")]
        public ReadOnlyMemory<byte> Sealed { get; set; }
    }
}
