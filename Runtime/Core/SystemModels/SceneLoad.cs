using System.Collections.Generic;
using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    [NetworkChannel("net", Channel.ReliableOrdered)]
    public class SceneLoad
    {
        [Network("bool")]
        [Codec("net")]
        public bool Full { get; set; }

        [Network("Array<u32>")]
        [Codec("net")]
        public List<uint> Scenes { get; set; } = new List<uint>();
    }
}
