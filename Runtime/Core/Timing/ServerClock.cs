using System;
using System.Collections.Generic;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;

namespace Fomoxa.Networking.Timing
{
    public sealed class ServerClock
    {
        private readonly ServerSession session;
        private readonly ClockProtocol protocol;
        private readonly Dictionary<ulong, ushort> reportedRtts = new Dictionary<ulong, ushort>();
        private readonly TickPong pong = new TickPong();
        private TickPing ping = new TickPing();

        public ServerClock(ServerSession session, ClockProtocol protocol)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
            session.Dispatcher.Register(protocol.PingCodec.MessageId, OnPing);
            session.OnRemoteConnectionState += OnPeerState;
            session.OnServerConnectionState += OnServerState;
        }

        public uint Tick { get; set; }

        internal Func<ulong, short> InputLeadOf { get; set; }

        public bool HasRtt(ulong peerId) => reportedRtts.ContainsKey(peerId);

        public TimeSpan RttOf(ulong peerId) =>
            reportedRtts.TryGetValue(peerId, out ushort milliseconds) ? TimeSpan.FromMilliseconds(milliseconds) : TimeSpan.Zero;

        private void OnPing(ulong peerId, ReadOnlyMemory<byte> payload)
        {
            protocol.PingCodec.Decode(payload, ref ping);
            if (ping.Rtt != 0)
            {
                reportedRtts[peerId] = ping.Rtt;
            }

            pong.ClientTime = ping.ClientTime;
            pong.ServerTick = Tick;
            pong.InputLead = InputLeadOf?.Invoke(peerId) ?? ClockProtocol.NoInputLead;
            session.Send(peerId, protocol.PongCodec.MessageId, protocol.PongCodec.Encode(pong).Span);
        }

        private void OnPeerState(ConnectionStateArgs args)
        {
            if (args.State == ConnectionState.Stopped)
            {
                reportedRtts.Remove(args.PeerId);
            }
        }

        private void OnServerState(ServerConnectionStateArgs args)
        {
            if (args.State == ServerState.Stopped)
            {
                reportedRtts.Clear();
            }
        }
    }
}
