using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    [NetworkChannel("net", Channel.ReliableOrdered)]
    public class LocalPeer
    {
        [Network("u64")]
        [Codec("net")]
        public ulong PeerId { get; set; }

        [Network("u16")]
        [Codec("net")]
        public ushort TickRate { get; set; }

        [Network("u8")]
        [Codec("net")]
        public byte PhysicsBackend { get; set; }
    }
}
