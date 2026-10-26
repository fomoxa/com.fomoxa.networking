using System;
using System.Net;
using System.Net.Sockets;
using Fomoxa.Net;
using Fomoxa.Net.Transports;

namespace Fomoxa.Networking.Transports
{
    public sealed class TcpTransportFactory : ITransportFactory
    {
        public TcpTransportFactory(TlsSettings tls = null)
        {
            Tls = tls;
        }

        public TlsSettings Tls { get; }

        public int FrameBudget => FomoxaWire.MaxDataFrameSize;

        public IListenerTransport CreateListener(ushort port, out ushort boundPort)
        {
            var listener = new TcpListenerTransport(new IPEndPoint(IPAddress.Any, port));
            boundPort = (ushort)listener.LocalEndPoint.Port;
            return listener;
        }

        public ITransportConnector CreateConnector(string address, ushort port) =>
            Tls == null ? new TcpConnector(address, port) : (ITransportConnector)new TlsTcpConnector(new HostResolver(address), port, address, Tls);
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

    internal sealed class TlsTcpConnector : ITransportConnector, ITlsRejection
    {
        private readonly SocketConnector connector;
        private readonly string host;
        private readonly TlsSettings settings;
        private TlsChannel channel;
        private TimeSpan started;
        private bool finished;

        public TlsTcpConnector(IHostResolver resolver, ushort port, string host, TlsSettings settings)
        {
            connector = new SocketConnector(resolver, port);
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public string TlsRejection { get; private set; }

        public ConnectStatus Poll(out ITransport transport)
        {
            transport = null;
            if (finished)
            {
                return ConnectStatus.Failed;
            }

            if (channel == null)
            {
                ConnectStatus connected = connector.Poll(out Socket socket);
                if (connected != ConnectStatus.Connected)
                {
                    return connected == ConnectStatus.Failed ? Fail() : ConnectStatus.Pending;
                }

                channel = new TlsChannel(socket, host, settings);
                started = settings.Clock();
            }

            switch (channel.Handshake())
            {
                case TlsHandshakeStatus.Completed:
                    transport = new TlsTcpTransport(channel);
                    channel = null;
                    finished = true;
                    return ConnectStatus.Connected;
                case TlsHandshakeStatus.Rejected:
                    TlsRejection = channel.Rejection;
                    Dispose();
                    return ConnectStatus.TlsRejected;
                case TlsHandshakeStatus.Failed:
                    return Fail();
                default:
                    return settings.Clock() - started > settings.HandshakeTimeout ? Fail() : ConnectStatus.Pending;
            }
        }

        public void Dispose()
        {
            finished = true;
            connector.Dispose();
            channel?.Dispose();
            channel = null;
        }

        private ConnectStatus Fail()
        {
            Dispose();
            return ConnectStatus.Failed;
        }
    }
}
