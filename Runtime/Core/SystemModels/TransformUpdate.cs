using System.Collections.Generic;
using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    public class TransformUpdate
    {
        [Network("u32")]
        [Codec("net")]
        public uint Tick { get; set; }

        [Network("u8")]
        [Codec("net")]
        public byte Mask { get; set; }

        [Network("Array<f32>")]
        [Codec("net")]
        public List<float> Values { get; set; } = new List<float>();

        [Network("u8")]
        [Codec("net")]
        public byte Generation { get; set; }
    }
}
