using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using Fomoxa.Net.WebSockets;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace Fomoxa.Networking.Transports
{
    internal enum TlsHandshakeStatus
    {
        Pending,
        Completed,
        Failed,
        Rejected,
    }

    internal sealed class TlsChannel : IByteStream
    {
        private const int ChunkBytes = 16 * 1024;

        private readonly Socket socket;
        private readonly TlsClientProtocol protocol = new TlsClientProtocol();
        private readonly Client client;
        private readonly byte[] received = new byte[ChunkBytes];
        private readonly byte[] plain = new byte[ChunkBytes];
        private byte[] written = new byte[ChunkBytes];
        private byte[] pending = new byte[ChunkBytes];
        private int pendingStart;
        private int pendingEnd;
        private bool failed;
        private bool peerClosed;
        private bool released;

        public TlsChannel(Socket socket, string host, TlsSettings settings)
        {
            this.socket = socket ?? throw new ArgumentNullException(nameof(socket));
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            socket.Blocking = false;
            socket.NoDelay = true;
            client = new Client(new BcTlsCrypto(new SecureRandom()), host, settings.CertificateCheck);
            protocol.Connect(client);
        }

        public string Rejection { get; private set; }

        public ProtocolVersion Version => client.Version;

        public TlsHandshakeStatus Handshake()
        {
            while (true)
            {
                if (Rejection != null)
                {
                    return TlsHandshakeStatus.Rejected;
                }

                Flush();
                if (failed)
                {
                    return TlsHandshakeStatus.Failed;
                }

                if (client.Completed)
                {
                    return TlsHandshakeStatus.Completed;
                }

                int read = socket.Receive(received, 0, received.Length, SocketFlags.None, out SocketError error);
                if (error == SocketError.WouldBlock)
                {
                    return TlsHandshakeStatus.Pending;
                }

                if (error != SocketError.Success || read == 0)
                {
                    failed = true;
                    return TlsHandshakeStatus.Failed;
                }

                OfferDuringHandshake(read);
            }
        }

        public int Send(ReadOnlySpan<byte> bytes, out SocketError error)
        {
            if (failed || !Flush())
            {
                error = failed ? SocketError.SocketError : SocketError.WouldBlock;
                return 0;
            }

            if (written.Length < bytes.Length)
            {
                written = new byte[bytes.Length];
            }

            bytes.CopyTo(written);
            try
            {
                protocol.WriteApplicationData(written, 0, bytes.Length);
            }
            catch (IOException)
            {
                failed = true;
                error = SocketError.SocketError;
                return 0;
            }

            Flush();
            error = failed ? SocketError.SocketError : SocketError.Success;
            return failed ? 0 : bytes.Length;
        }

        public int Receive(Span<byte> buffer, out SocketError error)
        {
            Flush();
            while (!failed)
            {
                int available = protocol.GetAvailableInputBytes();
                if (available > 0)
                {
                    int count = protocol.ReadInput(plain, 0, Math.Min(available, Math.Min(buffer.Length, plain.Length)));
                    new ReadOnlySpan<byte>(plain, 0, count).CopyTo(buffer);
                    error = count == 0 ? SocketError.WouldBlock : SocketError.Success;
                    return count;
                }

                if (peerClosed)
                {
                    error = SocketError.Success;
                    return 0;
                }

                int read = socket.Receive(received, 0, received.Length, SocketFlags.None, out error);
                if (error == SocketError.WouldBlock)
                {
                    return 0;
                }

                if (error != SocketError.Success)
                {
                    failed = true;
                    return 0;
                }

                if (read == 0)
                {
                    peerClosed = true;
                    continue;
                }

                try
                {
                    protocol.OfferInput(received, 0, read);
                }
                catch (IOException)
                {
                    failed = true;
                    break;
                }

                peerClosed = protocol.IsClosed;
                Flush();
            }

            error = SocketError.SocketError;
            return 0;
        }

        public void Shutdown()
        {
            if (released || failed)
            {
                return;
            }

            try
            {
                protocol.Close();
                Flush();
                socket.Shutdown(SocketShutdown.Send);
            }
            catch (IOException)
            {
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Dispose()
        {
            if (released)
            {
                return;
            }

            released = true;
            socket.Dispose();
        }

        private void OfferDuringHandshake(int read)
        {
            try
            {
                protocol.OfferInput(received, 0, read);
            }
            catch (TlsFatalAlertReceived alert)
            {
                Rejection = $"the TLS server {client.Host} ended the handshake with alert {AlertDescription.GetText(alert.AlertDescription)}";
            }
            catch (TlsFatalAlert) when (client.CertificateRejection != null)
            {
                Rejection = $"the TLS server {client.Host} was rejected: {client.CertificateRejection}";
            }
            catch (IOException)
            {
                failed = true;
            }

            if (Rejection != null)
            {
                Flush();
            }
        }

        private bool Flush()
        {
            int available = protocol.GetAvailableOutputBytes();
            if (available > 0)
            {
                if (pending.Length - pendingEnd < available)
                {
                    int waiting = pendingEnd - pendingStart;
                    byte[] target = pending.Length >= waiting + available ? pending : new byte[Math.Max(waiting + available, pending.Length * 2)];
                    Buffer.BlockCopy(pending, pendingStart, target, 0, waiting);
                    pending = target;
                    pendingStart = 0;
                    pendingEnd = waiting;
                }

                protocol.ReadOutput(pending, pendingEnd, available);
                pendingEnd += available;
            }

            while (pendingStart < pendingEnd)
            {
                if (!socket.Poll(0, SelectMode.SelectWrite))
                {
                    return false;
                }

                int sent = socket.Send(pending, pendingStart, pendingEnd - pendingStart, SocketFlags.None, out SocketError error);
                if (error != SocketError.Success && error != SocketError.WouldBlock)
                {
                    failed = true;
                    return false;
                }

                if (sent <= 0)
                {
                    return false;
                }

                pendingStart += sent;
            }

            pendingStart = 0;
            pendingEnd = 0;
            return true;
        }

        private sealed class Client : DefaultTlsClient
        {
            private readonly ITlsCertificateCheck check;
            private readonly string serverName;

            public Client(TlsCrypto crypto, string host, ITlsCertificateCheck check)
                : base(crypto)
            {
                Host = host;
                this.check = check;
                serverName = TlsHostName.ServerNameIndication(host);
            }

            public string Host { get; }

            public bool Completed { get; private set; }

            public string CertificateRejection { get; private set; }

            public ProtocolVersion Version { get; private set; }

            public override TlsAuthentication GetAuthentication() => new Authentication(this);

            public override void NotifyHandshakeComplete()
            {
                base.NotifyHandshakeComplete();
                Version = m_context.ServerVersion;
                Completed = true;
            }

            protected override ProtocolVersion[] GetSupportedVersions() => ProtocolVersion.TLSv13.DownTo(ProtocolVersion.TLSv12);

            protected override IList<ServerName> GetSniServerNames() =>
                serverName == null ? null : new List<ServerName> { new ServerName(NameType.host_name, Encoding.ASCII.GetBytes(serverName)) };

            private void CheckServer(TlsServerCertificate serverCertificate)
            {
                TlsCertificate[] certificates = serverCertificate.Certificate.GetCertificateList();
                var chain = new byte[certificates.Length][];
                for (int index = 0; index < certificates.Length; index++)
                {
                    chain[index] = certificates[index].GetEncoded();
                }

                string rejection;
                bool accepted;
                try
                {
                    accepted = check.Accepts(Host, chain, out rejection);
                }
                catch (Exception error)
                {
                    accepted = false;
                    rejection = $"the certificate check threw {error.GetType().Name}: {error.Message}";
                }

                if (!accepted)
                {
                    CertificateRejection = rejection ?? "the certificate check rejected the certificate";
                    throw new TlsFatalAlert(AlertDescription.bad_certificate);
                }
            }

            private sealed class Authentication : TlsAuthentication
            {
                private readonly Client owner;

                public Authentication(Client owner)
                {
                    this.owner = owner;
                }

                public void NotifyServerCertificate(TlsServerCertificate serverCertificate) => owner.CheckServer(serverCertificate);

                public TlsCredentials GetClientCredentials(CertificateRequest certificateRequest) => null;
            }
        }
    }
}
