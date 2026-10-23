using System;
using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    [NetworkChannel("net", Channel.ReliableOrdered)]
    public class FragmentStart
    {
        [Network("u32")]
        [Codec("net")]
        public uint MessageId { get; set; }

        [Network("u32")]
        [Codec("net")]
        public uint Length { get; set; }

        [Network("bytes")]
        [Codec("net")]
        public ReadOnlyMemory<byte> Data { get; set; }
    }
}
