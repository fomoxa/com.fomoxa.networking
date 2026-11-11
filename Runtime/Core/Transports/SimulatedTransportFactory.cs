using System;
using Fomoxa.Net.Transports;

namespace Fomoxa.Networking.Transports
{
    public sealed class SimulatedTransportFactory : IConnectionTransportFactory
    {
        private readonly ITransportFactory inner;
        private readonly NetworkConditions conditions;
        private readonly Func<TimeSpan> clock;
        private readonly Random seeds;

        public SimulatedTransportFactory(ITransportFactory inner, NetworkConditions conditions, Func<TimeSpan> clock, int? seed = null)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            seeds = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public int FrameBudget => inner.FrameBudget;

        public bool RequiresConnection => inner is IConnectionTransportFactory connections && connections.RequiresConnection;

        public IListenerTransport CreateListener(ushort port, out ushort boundPort) =>
            new SimulatedListener(inner.CreateListener(port, out boundPort), this);

        public ITransportConnector CreateConnector(string address, ushort port) =>
            new SimulatedConnector(inner.CreateConnector(address, port), this);

        public bool CanUseConnection(ReadOnlyMemory<byte> connection) =>
            inner is IConnectionTransportFactory connections && connections.CanUseConnection(connection);

        public ITransportConnector CreateConnector(string address, ushort port, ReadOnlyMemory<byte> connection) =>
            inner is IConnectionTransportFactory connections
                ? new SimulatedConnector(connections.CreateConnector(address, port, connection), this)
                : throw new ArgumentException("the simulated transport does not take a connection", nameof(connection));

        internal ITransport Wrap(ITransport transport) =>
            new SimulatedTransport(transport, conditions, clock, new Random(seeds.Next()));
    }

    internal sealed class SimulatedListener : IListenerTransport
    {
        private readonly SimulatedTransportFactory factory;

        public SimulatedListener(IListenerTransport inner, SimulatedTransportFactory factory)
        {
            Inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.factory = factory;
        }

        public IListenerTransport Inner { get; }

        public AcceptOutcome Accept()
        {
            AcceptOutcome outcome = Inner.Accept();
            return outcome.Status == AcceptStatus.Accepted ? AcceptOutcome.Accepted(factory.Wrap(outcome.Transport)) : outcome;
        }

        public void Dispose() => Inner.Dispose();
    }

    internal sealed class SimulatedConnector : ITransportConnector, ITlsRejection
    {
        private readonly ITransportConnector inner;
        private readonly SimulatedTransportFactory factory;

        public SimulatedConnector(ITransportConnector inner, SimulatedTransportFactory factory)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.factory = factory;
        }

        public string TlsRejection => (inner as ITlsRejection)?.TlsRejection;

        public ConnectStatus Poll(out ITransport transport)
        {
            ConnectStatus status = inner.Poll(out ITransport connected);
            transport = status == ConnectStatus.Connected ? factory.Wrap(connected) : connected;
            return status;
        }

        public void Dispose() => inner.Dispose();
    }
}
