using System.Net;
using System.Net.Sockets;
using Fomoxa.Net.Transports;

namespace Fomoxa.Networking.Transports
{
    public sealed class UdpTransportFactory : ITransportFactory
    {
        public int FrameBudget => 1200;

        public IListenerTransport CreateListener(ushort port, out ushort boundPort)
        {
            var listener = new UdpServerTransport(new IPEndPoint(IPAddress.Any, port));
            boundPort = (ushort)listener.LocalEndPoint.Port;
            return listener;
        }

        public ITransportConnector CreateConnector(string address, ushort port) =>
            new UdpConnector(address, port);
    }

    internal sealed class UdpConnector : ITransportConnector
    {
        private readonly IHostResolver resolver;
        private readonly ushort port;
        private bool finished;

        public UdpConnector(string host, ushort port)
            : this(new HostResolver(host), port)
        {
        }

        public UdpConnector(IHostResolver resolver, ushort port)
        {
            this.resolver = resolver;
            this.port = port;
        }

        public ConnectStatus Poll(out ITransport transport)
        {
            transport = null;
            if (finished)
            {
                return ConnectStatus.Failed;
            }

            ResolveStatus resolved = resolver.Poll(out IPAddress[] addresses);
            if (resolved == ResolveStatus.Pending)
            {
                return ConnectStatus.Pending;
            }

            finished = true;
            if (resolved == ResolveStatus.Failed)
            {
                return ConnectStatus.Failed;
            }

            try
            {
                transport = UdpTransport.Connect(new IPEndPoint(addresses[0], port));
                return ConnectStatus.Connected;
            }
            catch (SocketException)
            {
                return ConnectStatus.Failed;
            }
        }

        public void Dispose()
        {
            finished = true;
        }
    }
}
