using System;
using System.Net;
using System.Net.Sockets;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Networking.Transports
{
    public sealed class EncryptedUdpTransportFactory : IConnectionTransportFactory
    {
        public const int DatagramBudget = 1208;

        private readonly IMessageCodec<ConnectionGrant> grantCodec;
        private readonly IMessageCodec<ConnectToken> tokenCodec;
        private readonly ConnectTokens tokens;
        private readonly string target;

        public EncryptedUdpTransportFactory(FomoxaRegistry registry)
            : this(registry, null, null)
        {
        }

        public EncryptedUdpTransportFactory(FomoxaRegistry registry, ConnectTokens tokens, string target)
            : this(registry, tokens, target, false)
        {
        }

        public EncryptedUdpTransportFactory(FomoxaRegistry registry, ConnectTokens tokens, string target, bool behindGateway)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (tokens != null && target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            grantCodec = ConnectTokens.CodecOf<ConnectionGrant>(registry);
            tokenCodec = ConnectTokens.CodecOf<ConnectToken>(registry);
            this.tokens = tokens;
            this.target = target;
            BehindGateway = behindGateway;
        }

        public bool BehindGateway { get; }

        public int FrameBudget => DatagramBudget - EncryptedDatagram.DataOverhead;

        public bool RequiresConnection => true;

        public bool CanUseConnection(ReadOnlyMemory<byte> connection) => TryReadConnection(connection, out _, out _);

        public IListenerTransport CreateListener(ushort port, out ushort boundPort)
        {
            if (tokens == null)
            {
                throw new InvalidOperationException("an encrypted UDP listener needs the ConnectTokens and the target name of this server");
            }

            var listener = new EncryptedUdpListener(new IPEndPoint(IPAddress.Any, port), tokens, target, BehindGateway);
            boundPort = (ushort)listener.LocalEndPoint.Port;
            return listener;
        }

        public ITransportConnector CreateConnector(string address, ushort port) =>
            throw new InvalidOperationException("an encrypted UDP connection needs a connection from ConnectTokens.Issue; call StartConnection(address, port, connection)");

        public ITransportConnector CreateConnector(string address, ushort port, ReadOnlyMemory<byte> connection)
        {
            if (!TryReadConnection(connection, out ConnectionGrant grant, out ulong sessionId))
            {
                throw new ArgumentException("the connection is not a connection issued by ConnectTokens", nameof(connection));
            }

            return new EncryptedUdpConnector(new HostResolver(address), port, sessionId, grant);
        }

        private bool TryReadConnection(ReadOnlyMemory<byte> connection, out ConnectionGrant grant, out ulong sessionId)
        {
            sessionId = 0;
            if (!ConnectTokens.TryReadGrant(grantCodec, connection, out grant)
                || grant.Token.Length > ushort.MaxValue)
            {
                return false;
            }

            var token = new ConnectToken();
            try
            {
                tokenCodec.Decode(grant.Token, ref token);
            }
            catch (MessageDecodeException)
            {
                return false;
            }

            sessionId = token.SessionId;
            return sessionId != 0;
        }
    }

    internal sealed class EncryptedUdpConnector : ITransportConnector
    {
        private readonly IHostResolver resolver;
        private readonly ushort port;
        private readonly ulong sessionId;
        private readonly ConnectionGrant grant;
        private bool finished;

        public EncryptedUdpConnector(IHostResolver resolver, ushort port, ulong sessionId, ConnectionGrant grant)
        {
            this.resolver = resolver;
            this.port = port;
            this.sessionId = sessionId;
            this.grant = grant;
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
                var remote = new IPEndPoint(addresses[0], port);
                Socket socket = UdpSockets.Create(remote.AddressFamily);
                socket.Bind(new IPEndPoint(UdpSockets.AnyAddress(remote.AddressFamily), 0));
                transport = new EncryptedUdpTransport(socket, remote, sessionId, grant);
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
