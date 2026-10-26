using System;
using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    public class ConnectionGrant
    {
        [Network("bytes")]
        [Codec("net")]
        public ReadOnlyMemory<byte> Token { get; set; }

        [Network("bytes")]
        [Codec("net")]
        public ReadOnlyMemory<byte> ClientKey { get; set; }

        [Network("bytes")]
        [Codec("net")]
        public ReadOnlyMemory<byte> ServerKey { get; set; }
    }
}
