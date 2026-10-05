using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    public class ReliableAck
    {
        [Network("u16")]
        [Codec("net")]
        public ushort Next { get; set; }

        [Network("u32")]
        [Codec("net")]
        public uint Received { get; set; }
    }
}
