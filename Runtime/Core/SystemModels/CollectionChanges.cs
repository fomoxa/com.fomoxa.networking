using System;
using System.Collections.Generic;
using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    [NetworkChannel("net", Channel.ReliableOrdered)]
    public class CollectionChanges
    {
        [Network("u8")]
        [Codec("net")]
        public byte Collection { get; set; }

        [Network("u32")]
        [Codec("net")]
        public uint First { get; set; }

        [Network("bytes")]
        [Codec("net")]
        public ReadOnlyMemory<byte> Kinds { get; set; }

        [Network("Array<u32>")]
        [Codec("net")]
        public List<uint> Indexes { get; set; } = new List<uint>();

        [Network("Array<bytes>")]
        [Codec("net")]
        public List<ReadOnlyMemory<byte>> Keys { get; set; } = new List<ReadOnlyMemory<byte>>();

        [Network("Array<bytes>")]
        [Codec("net")]
        public List<ReadOnlyMemory<byte>> Values { get; set; } = new List<ReadOnlyMemory<byte>>();
    }
}
