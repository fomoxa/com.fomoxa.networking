using System;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Networking.Transports
{
    public sealed class EncryptedWebSocketTransportFactory : IConnectionTransportFactory
    {
        private readonly IMessageCodec<ConnectionGrant> grantCodec;
        private readonly IMessageCodec<ConnectToken> tokenCodec;
        private readonly ITransportFactory webSocket;

        public EncryptedWebSocketTransportFactory(FomoxaRegistry registry, WebSocketSettings settings = null)
            : this(registry, new WebSocketTransportFactory(settings))
        {
        }

        internal EncryptedWebSocketTransportFactory(FomoxaRegistry registry, ITransportFactory webSocket)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            grantCodec = ConnectTokens.CodecOf<ConnectionGrant>(registry);
            tokenCodec = ConnectTokens.CodecOf<ConnectToken>(registry);
            this.webSocket = webSocket ?? throw new ArgumentNullException(nameof(webSocket));
        }

        public int FrameBudget => EncryptedUdpTransportFactory.DatagramBudget - EncryptedDatagram.DataOverhead;

        public bool RequiresConnection => true;

        public bool CanUseConnection(ReadOnlyMemory<byte> connection) =>
            EncryptedUdpTransportFactory.TryReadConnection(grantCodec, tokenCodec, connection, out _, out _);

        public IListenerTransport CreateListener(ushort port, out ushort boundPort) =>
            throw new InvalidOperationException("an encrypted WebSocket transport only connects to a gateway; a pod listens with encrypted UDP behind the gateway");

        public ITransportConnector CreateConnector(string address, ushort port) =>
            throw new InvalidOperationException("an encrypted WebSocket connection needs a connection from ConnectTokens.Issue; call StartConnection(address, port, connection)");

        public ITransportConnector CreateConnector(string address, ushort port, ReadOnlyMemory<byte> connection)
        {
            if (!EncryptedUdpTransportFactory.TryReadConnection(grantCodec, tokenCodec, connection, out ConnectionGrant grant, out ulong sessionId))
            {
                throw new ArgumentException("the connection is not a connection issued by ConnectTokens", nameof(connection));
            }

            return new EncryptedWebSocketConnector(webSocket.CreateConnector(address, port), sessionId, grant);
        }
    }

    internal sealed class EncryptedWebSocketConnector : ITransportConnector, ITlsRejection
    {
        private readonly ITransportConnector webSocket;
        private readonly ulong sessionId;
        private readonly ConnectionGrant grant;

        public EncryptedWebSocketConnector(ITransportConnector webSocket, ulong sessionId, ConnectionGrant grant)
        {
            this.webSocket = webSocket;
            this.sessionId = sessionId;
            this.grant = grant;
        }

        public string TlsRejection => (webSocket as ITlsRejection)?.TlsRejection;

        public ConnectStatus Poll(out ITransport transport)
        {
            ConnectStatus status = webSocket.Poll(out ITransport opened);
            transport = status == ConnectStatus.Connected
                ? new EncryptedDatagramTransport(new MessageDatagramLink(opened), sessionId, grant)
                : null;
            return status;
        }

        public void Dispose() => webSocket.Dispose();
    }
}
