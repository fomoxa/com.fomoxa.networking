using System;
using System.Collections.Generic;
using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    public class BundleEntry
    {
        [Network("u32")]
        [Codec("net")]
        public uint MessageId { get; set; }

        [Network("bytes")]
        [Codec("net")]
        public ReadOnlyMemory<byte> Data { get; set; }
    }

    [Network]
    [Codec("net")]
    public class MessageBundle
    {
        [Network("Array<BundleEntry>")]
        [Codec("net")]
        public List<BundleEntry> Entries { get; set; } = new List<BundleEntry>();
    }
}
