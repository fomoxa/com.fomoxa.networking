using System;
using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    [NetworkChannel("net", Channel.ReliableOrdered)]
    public class PeerTransfer
    {
        [Network("string")]
        [Codec("net")]
        public string Address { get; set; } = string.Empty;

        [Network("u16")]
        [Codec("net")]
        public ushort Port { get; set; }

        [Network("bytes")]
        [Codec("net")]
        public ReadOnlyMemory<byte> Ticket { get; set; }

        [Network("bytes")]
        [Codec("net")]
        public ReadOnlyMemory<byte> Connection { get; set; }
    }
}
