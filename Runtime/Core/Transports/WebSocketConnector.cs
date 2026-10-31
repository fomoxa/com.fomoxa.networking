using System;
using System.Net.Sockets;
using Fomoxa.Net.Transports;
using Fomoxa.Net.WebSockets;

namespace Fomoxa.Networking.Transports
{
    internal sealed class WebSocketConnector : ITransportConnector, ITlsRejection
    {
        private readonly SocketConnector connector;
        private readonly WebSocketSettings settings;
        private readonly string target;
        private readonly string host;
        private readonly string tlsHost;
        private readonly TlsSettings tls;
        private TlsChannel handshaking;
        private WebSocketClientHandshake upgrading;
        private TimeSpan started;
        private bool finished;

        public WebSocketConnector(IHostResolver resolver, ushort port, string target, string host, WebSocketSettings settings)
            : this(resolver, port, target, host, settings, null, null)
        {
        }

        public WebSocketConnector(IHostResolver resolver, ushort port, string target, string host, WebSocketSettings settings, string tlsHost, TlsSettings tls)
        {
            connector = new SocketConnector(resolver, port);
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.target = target;
            this.host = host;
            this.tlsHost = tlsHost;
            this.tls = tls;
        }

        public string TlsRejection { get; private set; }

        public ConnectStatus Poll(out ITransport transport)
        {
            transport = null;
            if (finished)
            {
                return ConnectStatus.Failed;
            }

            if (handshaking == null && upgrading == null)
            {
                ConnectStatus connected = connector.Poll(out Socket socket);
                if (connected != ConnectStatus.Connected)
                {
                    return connected == ConnectStatus.Failed ? Fail() : ConnectStatus.Pending;
                }

                started = settings.Clock();
                if (tls == null)
                {
                    upgrading = Upgrade(new SocketByteStream(socket));
                }
                else
                {
                    handshaking = new TlsChannel(socket, tlsHost, tls);
                }
            }

            if (handshaking != null)
            {
                switch (handshaking.Handshake())
                {
                    case TlsHandshakeStatus.Completed:
                        upgrading = Upgrade(handshaking);
                        handshaking = null;
                        break;
                    case TlsHandshakeStatus.Rejected:
                        TlsRejection = handshaking.Rejection;
                        Dispose();
                        return ConnectStatus.TlsRejected;
                    case TlsHandshakeStatus.Failed:
                        return Fail();
                    default:
                        return TimedOut() ? Fail() : ConnectStatus.Pending;
                }
            }

            if (TimedOut())
            {
                return Fail();
            }

            switch (upgrading.Poll(out WebSocketTransport upgraded))
            {
                case WebSocketConnectStatus.Connected:
                    transport = upgraded;
                    upgrading = null;
                    finished = true;
                    return ConnectStatus.Connected;
                case WebSocketConnectStatus.Failed:
                    return Fail();
                default:
                    return ConnectStatus.Pending;
            }
        }

        public void Dispose()
        {
            finished = true;
            connector.Dispose();
            handshaking?.Dispose();
            handshaking = null;
            upgrading?.Dispose();
            upgrading = null;
        }

        private WebSocketClientHandshake Upgrade(IByteStream stream) => new WebSocketClientHandshake(stream, target, host, settings.ForPackage());

        private bool TimedOut() => settings.Clock() - started > settings.HandshakeTimeout;

        private ConnectStatus Fail()
        {
            Dispose();
            return ConnectStatus.Failed;
        }
    }
}
