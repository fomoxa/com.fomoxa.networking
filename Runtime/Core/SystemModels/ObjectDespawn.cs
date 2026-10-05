using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    [NetworkChannel("net", Channel.ReliableOrdered)]
    public class ObjectDespawn
    {
        [Network("u32")]
        [Codec("net")]
        public uint ObjectId { get; set; }
    }
}
