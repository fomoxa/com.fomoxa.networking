using System;
using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    [NetworkChannel("net", Channel.ReliableOrdered)]
    public class FragmentPart
    {
        [Network("bytes")]
        [Codec("net")]
        public ReadOnlyMemory<byte> Data { get; set; }
    }
}
