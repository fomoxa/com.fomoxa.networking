using System;
using System.Collections.Generic;
using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    public class InputFrames
    {
        [Network("u32")]
        [Codec("net")]
        public uint Tick { get; set; }

        [Network("Array<bytes>")]
        [Codec("net")]
        public List<ReadOnlyMemory<byte>> Frames { get; set; } = new List<ReadOnlyMemory<byte>>();
    }
}
