using System;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Transports;

namespace Fomoxa.Unity
{
    public sealed class TransportManager
    {
        private readonly int loopbackCapacity;

        internal TransportManager(NetworkTransport transport, int loopbackCapacity)
        {
            Transport = transport;
            Factory = transport.CreateFactory()
                ?? throw new InvalidOperationException($"{transport.GetType().Name} returned no transport factory");
            this.loopbackCapacity = loopbackCapacity;
        }

        public NetworkTransport Transport { get; }

        public ITransportFactory Factory { get; }

        internal IListenerTransport CreateListener(ushort port, out LoopbackListener localListener, out ushort boundPort)
        {
            if (NetworkTransport.RunsInBrowser)
            {
                throw new PlatformNotSupportedException("a server cannot listen in a browser; WebGL builds are clients only");
            }

            IListenerTransport network = Factory.CreateListener(port, out boundPort);
            localListener = new LoopbackListener(loopbackCapacity);
            return new CompositeListener(new[] { network, localListener }, new[] { Factory.FrameBudget, Factory.FrameBudget });
        }

        internal ITransportConnector CreateConnector(string address, ushort port) =>
            Factory.CreateConnector(address, port);
    }
}
