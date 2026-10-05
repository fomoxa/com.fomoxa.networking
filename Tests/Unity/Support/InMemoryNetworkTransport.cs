using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Transports;

namespace Fomoxa.Unity.Tests.Support
{
    public sealed class InMemoryNetworkTransport : NetworkTransport
    {
        private InMemoryTransportFactory factory;

        public int ConnectorsCreated => factory?.ConnectorsCreated ?? 0;

        public override ITransportFactory CreateFactory() => factory ??= new InMemoryTransportFactory();

        private sealed class InMemoryTransportFactory : ITransportFactory
        {
            private LoopbackListener network;

            public int ConnectorsCreated { get; private set; }

            public int FrameBudget => FomoxaWire.MaxDataFrameSize;

            public IListenerTransport CreateListener(ushort port, out ushort boundPort)
            {
                boundPort = port;
                network = new LoopbackListener(64);
                return network;
            }

            public ITransportConnector CreateConnector(string address, ushort port)
            {
                ConnectorsCreated++;
                return new InMemoryConnector(this);
            }

            private sealed class InMemoryConnector : ITransportConnector
            {
                private readonly InMemoryTransportFactory owner;

                public InMemoryConnector(InMemoryTransportFactory owner)
                {
                    this.owner = owner;
                }

                public ConnectStatus Poll(out ITransport transport)
                {
                    transport = owner.network?.Connect();
                    return transport == null ? ConnectStatus.Failed : ConnectStatus.Connected;
                }

                public void Dispose()
                {
                }
            }
        }
    }
}
