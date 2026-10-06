using System;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Transports;

namespace Fomoxa.Unity
{
    public sealed class TransportManager
    {
        internal TransportManager(NetworkTransport transport)
        {
            Transport = transport;
            ITransportFactory factory = transport.CreateFactory()
                ?? throw new InvalidOperationException($"{transport.GetType().Name} returned no transport factory");
            Factory = new ListenerGuard(factory);
        }

        public NetworkTransport Transport { get; }

        public ITransportFactory Factory { get; }

        private sealed class ListenerGuard : ITransportFactory
        {
            private readonly ITransportFactory inner;

            public ListenerGuard(ITransportFactory inner)
            {
                this.inner = inner;
            }

            public int FrameBudget => inner.FrameBudget;

            public IListenerTransport CreateListener(ushort port, out ushort boundPort)
            {
                if (NetworkTransport.RunsInBrowser)
                {
                    throw new PlatformNotSupportedException("a server cannot listen in a browser; WebGL builds are clients only");
                }

                return inner.CreateListener(port, out boundPort);
            }

            public ITransportConnector CreateConnector(string address, ushort port) => inner.CreateConnector(address, port);
        }
    }
}
