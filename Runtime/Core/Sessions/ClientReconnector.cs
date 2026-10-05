using System;
using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Transports;

namespace Fomoxa.Networking.Sessions
{
    public sealed class ReconnectPolicy
    {
        public int MaxRetries { get; set; } = -1;

        public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(0.5);
    }

    public sealed class ClientReconnector
    {
        private readonly ClientSession session;
        private readonly ReconnectPolicy policy;

        private Func<ITransportConnector> connectorSource;
        private int retries;
        private TimeSpan? retryAt;
        private TimeSpan lastNow;

        public ClientReconnector(ClientSession session, ReconnectPolicy policy)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.policy = policy ?? new ReconnectPolicy();
            session.OnClientConnectionState += OnSessionState;
        }

        public event Action<ConnectionStateArgs> OnClientConnectionState;

        public void Start(Func<ITransportConnector> source, TimeSpan now)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            Reset(now);
            connectorSource = source;
            session.Start(source());
        }

        public void StartWithoutRetry(ITransport transport, TimeSpan now)
        {
            Reset(now);
            session.Start(transport, now);
        }

        public void Tick(TimeSpan now)
        {
            lastNow = now;
            if (retryAt.HasValue && now >= retryAt.Value)
            {
                retryAt = null;
                session.Start(connectorSource());
            }

            session.Tick(now);
        }

        public void Stop()
        {
            connectorSource = null;
            retryAt = null;
            session.Stop();
        }

        private void Reset(TimeSpan now)
        {
            connectorSource = null;
            retryAt = null;
            retries = 0;
            lastNow = now;
        }

        private void OnSessionState(ConnectionStateArgs args)
        {
            if (args.State == ConnectionState.Started)
            {
                retries = 0;
            }

            bool willRetry = args.State == ConnectionState.Stopped && ShouldRetry(args);
            if (willRetry)
            {
                retries++;
                retryAt = lastNow + policy.Interval;
            }

            OnClientConnectionState?.Invoke(new ConnectionStateArgs(
                args.PeerId,
                args.State,
                args.Reason,
                args.Failure,
                args.DiscardedMessages,
                willRetry));
        }

        private bool ShouldRetry(ConnectionStateArgs args) =>
            connectorSource != null
            && args.Reason != StopReason.LocalStop
            && args.Reason != StopReason.HandlerException
            && args.Reason != StopReason.PrefabMismatch
            && args.Reason != StopReason.ObjectMismatch
            && args.Reason != StopReason.PhysicsBackendMismatch
            && (args.Reason != StopReason.HandshakeFailed || args.Failure == HandshakeFailure.Timeout)
            && (policy.MaxRetries < 0 || retries < policy.MaxRetries);
    }
}
