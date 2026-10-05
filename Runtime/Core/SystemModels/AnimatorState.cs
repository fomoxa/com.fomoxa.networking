using System.Collections.Generic;
using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    [NetworkChannel("net", Channel.ReliableOrdered)]
    public class AnimatorState
    {
        [Network("u32")]
        [Codec("net")]
        public uint Layout { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float Speed { get; set; }

        [Network("Array<f32>")]
        [Codec("net")]
        public List<float> Floats { get; set; } = new List<float>();

        [Network("Array<i32>")]
        [Codec("net")]
        public List<int> Ints { get; set; } = new List<int>();

        [Network("Array<bool>")]
        [Codec("net")]
        public List<bool> Bools { get; set; } = new List<bool>();

        [Network("Array<u8>")]
        [Codec("net")]
        public List<byte> Triggers { get; set; } = new List<byte>();

        [Network("Array<f32>")]
        [Codec("net")]
        public List<float> LayerWeights { get; set; } = new List<float>();

        [Network("Array<i32>")]
        [Codec("net")]
        public List<int> LayerStates { get; set; } = new List<int>();
    }
}
