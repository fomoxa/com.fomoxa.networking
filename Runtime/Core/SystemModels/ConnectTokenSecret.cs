using System;
using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    public class ConnectTokenSecret
    {
        [Network("string")]
        [Codec("net")]
        public string Target { get; set; } = string.Empty;

        [Network("bytes")]
        [Codec("net")]
        public ReadOnlyMemory<byte> ClientKey { get; set; }

        [Network("bytes")]
        [Codec("net")]
        public ReadOnlyMemory<byte> ServerKey { get; set; }

        [Network("bytes")]
        [Codec("net")]
        public ReadOnlyMemory<byte> Data { get; set; }
    }
}
