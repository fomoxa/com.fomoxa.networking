using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    [NetworkChannel("net", Channel.ReliableOrdered)]
    public class ObjectOwnerChange
    {
        [Network("u32")]
        [Codec("net")]
        public uint ObjectId { get; set; }

        [Network("u64")]
        [Codec("net")]
        public ulong OwnerId { get; set; }
    }
}
