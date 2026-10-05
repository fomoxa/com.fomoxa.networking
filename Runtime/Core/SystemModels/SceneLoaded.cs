using System.Collections.Generic;
using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    [NetworkChannel("net", Channel.ReliableOrdered)]
    public class SceneLoaded
    {
        [Network("Array<u32>")]
        [Codec("net")]
        public List<uint> Scenes { get; set; } = new List<uint>();
    }
}
