using System;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Networking.Timing
{
    public sealed class ClockProtocol
    {
        public const short NoInputLead = short.MinValue;

        public ClockProtocol(IMessageCodec<TickPing> pingCodec, IMessageCodec<TickPong> pongCodec)
        {
            PingCodec = pingCodec ?? throw new ArgumentNullException(nameof(pingCodec));
            PongCodec = pongCodec ?? throw new ArgumentNullException(nameof(pongCodec));
        }

        public IMessageCodec<TickPing> PingCodec { get; }

        public IMessageCodec<TickPong> PongCodec { get; }

        public static ClockProtocol From(FomoxaRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            return new ClockProtocol(registry.Codec<TickPing>(), registry.Codec<TickPong>());
        }
    }
}
