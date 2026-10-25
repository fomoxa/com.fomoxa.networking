using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    [NetworkChannel("net", Channel.ReliableOrdered)]
    public class SessionClose
    {
        [Network("u8")]
        [Codec("net")]
        public byte Reason { get; set; }
    }
}
