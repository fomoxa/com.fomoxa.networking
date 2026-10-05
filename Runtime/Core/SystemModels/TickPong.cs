using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    public class TickPong
    {
        [Network("u32")]
        [Codec("net")]
        public uint ClientTime { get; set; }

        [Network("u32")]
        [Codec("net")]
        public uint ServerTick { get; set; }

        [Network("i16")]
        [Codec("net")]
        public short InputLead { get; set; }
    }
}
