using System;
using System.Collections.Generic;
using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    [NetworkChannel("net", Channel.ReliableOrdered)]
    public class ObjectSpawn
    {
        [Network("u32")]
        [Codec("net")]
        public uint ObjectId { get; set; }

        [Network("u32")]
        [Codec("net")]
        public uint PrefabId { get; set; }

        [Network("u32")]
        [Codec("net")]
        public uint PrefabFingerprint { get; set; }

        [Network("u64")]
        [Codec("net")]
        public ulong OwnerId { get; set; }

        [Network("u8")]
        [Codec("net")]
        public byte Mask { get; set; }

        [Network("Array<f32>")]
        [Codec("net")]
        public List<float> Values { get; set; } = new List<float>();

        [Network("Array<bytes>")]
        [Codec("net")]
        public List<ReadOnlyMemory<byte>> States { get; set; } = new List<ReadOnlyMemory<byte>>();

        [Network("u32")]
        [Codec("net")]
        public uint SceneId { get; set; }
    }
}
