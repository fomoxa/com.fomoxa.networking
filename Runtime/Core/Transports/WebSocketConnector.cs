using System;
using System.Net.Sockets;
using Fomoxa.Net.Transports;

namespace Fomoxa.Networking.Transports
{
    internal sealed class WebSocketConnector : ITransportConnector, ITlsRejection
    {
        private readonly SocketConnector connector;
        private readonly WebSocketSettings settings;
        private readonly string tlsHost;
        private readonly TlsSettings tls;
        private readonly string key;
        private readonly byte[] request;
        private readonly byte[] response;
        private IByteStream stream;
        private TlsChannel handshaking;
        private TimeSpan started;
        private int requestSent;
        private int used;
        private bool finished;

        public WebSocketConnector(IHostResolver resolver, ushort port, string target, string host, WebSocketSettings settings)
            : this(resolver, port, target, host, settings, null, null)
        {
        }

        public WebSocketConnector(IHostResolver resolver, ushort port, string target, string host, WebSocketSettings settings, string tlsHost, TlsSettings tls)
        {
            connector = new SocketConnector(resolver, port);
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.tlsHost = tlsHost;
            this.tls = tls;
            key = WebSocketHandshake.NewKey();
            request = WebSocketHandshake.Request(target, host, key);
            response = new byte[settings.MaxHandshakeBytes];
        }

        public string TlsRejection { get; private set; }

        public ConnectStatus Poll(out ITransport transport)
        {
            transport = null;
            if (finished)
            {
                return ConnectStatus.Failed;
            }

            if (stream == null && handshaking == null)
            {
                ConnectStatus connected = connector.Poll(out Socket socket);
                if (connected != ConnectStatus.Connected)
                {
                    return connected == ConnectStatus.Failed ? Fail() : ConnectStatus.Pending;
                }

                started = settings.Clock();
                if (tls == null)
                {
                    stream = new SocketByteStream(socket);
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
                        stream = handshaking;
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

            if (!WriteRequest())
            {
                return finished ? ConnectStatus.Failed : ConnectStatus.Pending;
            }

            return ReadResponse(out transport);
        }

        public void Dispose()
        {
            finished = true;
            connector.Dispose();
            handshaking?.Dispose();
            handshaking = null;
            stream?.Dispose();
            stream = null;
        }

        private bool TimedOut() => settings.Clock() - started > settings.HandshakeTimeout;

        private bool WriteRequest()
        {
            while (requestSent < request.Length)
            {
                int sent = stream.Send(new ReadOnlySpan<byte>(request, requestSent, request.Length - requestSent), out SocketError error);
                if (error == SocketError.WouldBlock || (error == SocketError.Success && sent <= 0))
                {
                    return false;
                }

                if (error != SocketError.Success)
                {
                    Fail();
                    return false;
                }

                requestSent += sent;
            }

            return true;
        }

        private ConnectStatus ReadResponse(out ITransport transport)
        {
            transport = null;
            int read = stream.Receive(new Span<byte>(response, used, response.Length - used), out SocketError error);
            if (error == SocketError.WouldBlock)
            {
                return ConnectStatus.Pending;
            }

            if (error != SocketError.Success || read == 0)
            {
                return Fail();
            }

            used += read;
            int headerEnd = WebSocketHandshake.HeaderEnd(response, used);
            if (headerEnd < 0)
            {
                return used == response.Length ? Fail() : ConnectStatus.Pending;
            }

            if (!WebSocketHandshake.AcceptsResponse(response, headerEnd, key))
            {
                return Fail();
            }

            transport = new WebSocketTransport(stream, true, new ReadOnlySpan<byte>(response, headerEnd, used - headerEnd));
            stream = null;
            finished = true;
            return ConnectStatus.Connected;
        }

        private ConnectStatus Fail()
        {
            Dispose();
            return ConnectStatus.Failed;
        }
    }
}
