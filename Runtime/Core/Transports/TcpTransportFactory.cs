using System.Net;
using System.Net.Sockets;
using Fomoxa.Net;
using Fomoxa.Net.Transports;

namespace Fomoxa.Networking.Transports
{
    public sealed class TcpTransportFactory : ITransportFactory
    {
        public int FrameBudget => FomoxaWire.MaxDataFrameSize;

        public IListenerTransport CreateListener(ushort port, out ushort boundPort)
        {
            var listener = new TcpListenerTransport(new IPEndPoint(IPAddress.Any, port));
            boundPort = (ushort)listener.LocalEndPoint.Port;
            return listener;
        }

        public ITransportConnector CreateConnector(string address, ushort port) =>
            new TcpConnector(address, port);
    }

    internal sealed class TcpConnector : ITransportConnector
    {
        private readonly SocketConnector connector;

        public TcpConnector(string host, ushort port)
            : this(new HostResolver(host), port)
        {
        }

        public TcpConnector(IHostResolver resolver, ushort port)
        {
            connector = new SocketConnector(resolver, port);
        }

        public ConnectStatus Poll(out ITransport transport)
        {
            ConnectStatus status = connector.Poll(out Socket socket);
            transport = status == ConnectStatus.Connected ? new TcpTransport(socket) : null;
            return status;
        }

        public void Dispose() => connector.Dispose();
    }
}
