using System;
using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    public class TransferTicket
    {
        [Network("bytes")]
        [Codec("net")]
        public ReadOnlyMemory<byte> Data { get; set; }

        [Network("string")]
        [Codec("net")]
        public string Target { get; set; } = string.Empty;

        [Network("i64")]
        [Codec("net")]
        public long Expires { get; set; }

        [Network("bytes")]
        [Codec("net")]
        public ReadOnlyMemory<byte> Nonce { get; set; }

        [Network("bytes")]
        [Codec("net")]
        public ReadOnlyMemory<byte> Mac { get; set; }
    }
}
