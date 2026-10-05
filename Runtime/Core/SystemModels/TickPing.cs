using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    public class TickPing
    {
        [Network("u32")]
        [Codec("net")]
        public uint ClientTime { get; set; }

        [Network("u16")]
        [Codec("net")]
        public ushort Rtt { get; set; }
    }
}
