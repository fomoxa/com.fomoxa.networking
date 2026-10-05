using System;
using System.Collections.Generic;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;

namespace Fomoxa.Networking.Timing
{
    public sealed class ClientClock
    {
        public const int StartupPings = 3;

        private readonly ClientSession session;
        private readonly ClockProtocol protocol;
        private readonly ClockSettings settings;
        private readonly List<PongSample> pending = new List<PongSample>();
        private readonly TickPing ping = new TickPing();
        private TickPong pong = new TickPong();
        private int startupPingsLeft;
        private TimeSpan nextPing;

        public ClientClock(ClientSession session, ClockProtocol protocol, ClockSettings settings)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Estimator = new ClockEstimator(settings);
            session.Dispatcher.Register(protocol.PongCodec.MessageId, OnPong);
            session.OnClientConnectionState += OnState;
        }

        public ClockEstimator Estimator { get; }

        public void Update(TimeSpan now)
        {
            foreach (PongSample received in pending)
            {
                Estimator.AddSample(received.ClientTime, received.ServerTick, received.InputLead, now);
            }

            pending.Clear();
            if (session.State != ConnectionState.Started)
            {
                return;
            }

            if (startupPingsLeft == 0 && now < nextPing)
            {
                return;
            }

            if (startupPingsLeft > 0)
            {
                startupPingsLeft--;
            }

            nextPing = now + settings.PingInterval;
            ping.ClientTime = ClockEstimator.ClientTimeOf(now);
            ping.Rtt = Estimator.Synced ? (ushort)Math.Min(ushort.MaxValue, Math.Max(1, Math.Round(Estimator.Rtt.TotalMilliseconds))) : (ushort)0;
            session.Send(protocol.PingCodec.MessageId, protocol.PingCodec.Encode(ping).Span);
        }

        private void OnPong(ulong peerId, ReadOnlyMemory<byte> payload)
        {
            protocol.PongCodec.Decode(payload, ref pong);
            pending.Add(new PongSample(pong.ClientTime, pong.ServerTick, pong.InputLead));
        }

        private void OnState(ConnectionStateArgs args)
        {
            if (args.State == ConnectionState.Started)
            {
                startupPingsLeft = StartupPings;
                return;
            }

            if (args.State == ConnectionState.Stopped)
            {
                startupPingsLeft = 0;
                pending.Clear();
                Estimator.Reset();
            }
        }

        private readonly struct PongSample
        {
            public PongSample(uint clientTime, uint serverTick, short inputLead)
            {
                ClientTime = clientTime;
                ServerTick = serverTick;
                InputLead = inputLead;
            }

            public uint ClientTime { get; }

            public uint ServerTick { get; }

            public short InputLead { get; }
        }
    }
}
