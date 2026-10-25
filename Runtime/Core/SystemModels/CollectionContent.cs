using System;
using System.Collections.Generic;
using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    [NetworkChannel("net", Channel.ReliableOrdered)]
    public class CollectionContent
    {
        [Network("u8")]
        [Codec("net")]
        public byte Collection { get; set; }

        [Network("u8")]
        [Codec("net")]
        public byte Kind { get; set; }

        [Network("u32")]
        [Codec("net")]
        public uint KeyId { get; set; }

        [Network("u32")]
        [Codec("net")]
        public uint ValueId { get; set; }

        [Network("u32")]
        [Codec("net")]
        public uint Sequence { get; set; }

        [Network("Array<bytes>")]
        [Codec("net")]
        public List<ReadOnlyMemory<byte>> Keys { get; set; } = new List<ReadOnlyMemory<byte>>();

        [Network("Array<bytes>")]
        [Codec("net")]
        public List<ReadOnlyMemory<byte>> Values { get; set; } = new List<ReadOnlyMemory<byte>>();
    }

    [Network]
    [Codec("net")]
    public class CollectionContents
    {
        [Network("Array<CollectionContent>")]
        [Codec("net")]
        public List<CollectionContent> Collections { get; set; } = new List<CollectionContent>();
    }
}
