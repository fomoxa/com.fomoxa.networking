using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using Org.BouncyCastle.X509;

namespace Fomoxa.Networking.Tests
{
    internal sealed class TestCertificate
    {
        public TestCertificate(X509Certificate certificate, AsymmetricKeyParameter privateKey)
        {
            Certificate = certificate;
            PrivateKey = privateKey;
        }

        public X509Certificate Certificate { get; }

        public AsymmetricKeyParameter PrivateKey { get; }

        public byte[] Der => Certificate.GetEncoded();

        public string Pem => "-----BEGIN CERTIFICATE-----\n" + Convert.ToBase64String(Der) + "\n-----END CERTIFICATE-----\n";
    }

    internal sealed class LeafSpec
    {
        public string Subject { get; set; } = "leaf";

        public string[] DnsNames { get; set; } = { "localhost" };

        public string[] Addresses { get; set; } = { "127.0.0.1" };

        public KeyPurposeID[] Purposes { get; set; } = { KeyPurposeID.id_kp_serverAuth };

        public DateTime NotBefore { get; set; } = DateTime.UtcNow.AddDays(-1);

        public DateTime NotAfter { get; set; } = DateTime.UtcNow.AddDays(1);
    }

    internal static class TestCertificates
    {
        private static readonly SecureRandom Random = new SecureRandom();
        private static long serial = 1;

        public static TestCertificate Root(string name) => Issue(null, name, true, null, DateTime.UtcNow.AddDays(-10), DateTime.UtcNow.AddDays(10));

        public static TestCertificate Intermediate(TestCertificate issuer, string name) =>
            Issue(issuer, name, true, null, DateTime.UtcNow.AddDays(-10), DateTime.UtcNow.AddDays(10));

        public static TestCertificate Leaf(TestCertificate issuer, LeafSpec spec = null)
        {
            spec = spec ?? new LeafSpec();
            return Issue(issuer, spec.Subject, false, generator =>
            {
                var names = new List<GeneralName>();
                foreach (string dnsName in spec.DnsNames)
                {
                    names.Add(new GeneralName(GeneralName.DnsName, dnsName));
                }

                foreach (string address in spec.Addresses)
                {
                    names.Add(new GeneralName(GeneralName.IPAddress, address));
                }

                if (names.Count > 0)
                {
                    generator.AddExtension(X509Extensions.SubjectAlternativeName, false, new GeneralNames(names.ToArray()));
                }

                if (spec.Purposes != null)
                {
                    generator.AddExtension(X509Extensions.ExtendedKeyUsage, false, new ExtendedKeyUsage(spec.Purposes));
                }
            }, spec.NotBefore, spec.NotAfter);
        }

        private static TestCertificate Issue(TestCertificate issuer, string name, bool authority, Action<X509V3CertificateGenerator> extend, DateTime notBefore, DateTime notAfter)
        {
            var keys = new ECKeyPairGenerator();
            keys.Init(new ECKeyGenerationParameters(SecObjectIdentifiers.SecP256r1, Random));
            AsymmetricCipherKeyPair pair = keys.GenerateKeyPair();
            var subject = new X509Name("CN=" + name);
            var generator = new X509V3CertificateGenerator();
            generator.SetSerialNumber(BigInteger.ValueOf(Interlocked.Increment(ref serial)));
            generator.SetIssuerDN(issuer == null ? subject : issuer.Certificate.SubjectDN);
            generator.SetSubjectDN(subject);
            generator.SetNotBefore(notBefore);
            generator.SetNotAfter(notAfter);
            generator.SetPublicKey(pair.Public);
            generator.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(authority));
            generator.AddExtension(X509Extensions.KeyUsage, true, new KeyUsage(authority ? KeyUsage.KeyCertSign | KeyUsage.CrlSign : KeyUsage.DigitalSignature));
            extend?.Invoke(generator);
            AsymmetricKeyParameter signer = issuer == null ? pair.Private : issuer.PrivateKey;
            return new TestCertificate(generator.Generate(new Asn1SignatureFactory("SHA256WITHECDSA", signer, Random)), pair.Private);
        }
    }

    internal sealed class TlsTestProxy : IDisposable
    {
        private readonly Socket listener;
        private readonly int backendPort;
        private readonly byte[][] chain;
        private readonly AsymmetricKeyParameter privateKey;
        private readonly Thread thread;
        private readonly object gate = new object();
        private readonly List<string> serverNames = new List<string>();
        private readonly List<ProtocolVersion> versions = new List<ProtocolVersion>();
        private readonly List<Thread> servingThreads = new List<Thread>();
        private readonly List<Exception> errors = new List<Exception>();
        private volatile bool stopping;
        private int closeNotifies;

        public TlsTestProxy(int backendPort, TestCertificate leaf, params TestCertificate[] intermediates)
        {
            this.backendPort = backendPort;
            privateKey = leaf.PrivateKey;
            chain = new byte[intermediates.Length + 1][];
            chain[0] = leaf.Der;
            for (int index = 0; index < intermediates.Length; index++)
            {
                chain[index + 1] = intermediates[index].Der;
            }

            listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(16);
            thread = new Thread(AcceptLoop) { IsBackground = true };
            thread.Start();
        }

        public ushort Port => (ushort)((IPEndPoint)listener.LocalEndPoint).Port;

        public ProtocolVersion[] Versions { get; set; }

        public bool RefuseHandshake { get; set; }

        public bool CloseAfterHandshake { get; set; }

        public bool KeepSocketAfterClose { get; set; }

        public int CloseNotifies
        {
            get
            {
                lock (gate)
                {
                    return closeNotifies;
                }
            }
        }

        public IReadOnlyList<string> ServerNames
        {
            get
            {
                lock (gate)
                {
                    return serverNames.ToArray();
                }
            }
        }

        public IReadOnlyList<Exception> Errors
        {
            get
            {
                lock (gate)
                {
                    return errors.ToArray();
                }
            }
        }

        public IReadOnlyList<ProtocolVersion> NegotiatedVersions
        {
            get
            {
                lock (gate)
                {
                    return versions.ToArray();
                }
            }
        }

        public void Dispose()
        {
            stopping = true;
            thread.Join(TimeSpan.FromSeconds(5));
            Thread[] serving;
            lock (gate)
            {
                serving = servingThreads.ToArray();
            }

            foreach (Thread each in serving)
            {
                each.Join(TimeSpan.FromSeconds(5));
            }

            listener.Dispose();
        }

        private void AcceptLoop()
        {
            try
            {
                while (!stopping)
                {
                    if (!listener.Poll(1000, SelectMode.SelectRead))
                    {
                        continue;
                    }

                    Socket accepted = listener.Accept();
                    var serving = new Thread(() => Serve(accepted)) { IsBackground = true };
                    lock (gate)
                    {
                        servingThreads.Add(serving);
                    }

                    serving.Start();
                }
            }
            catch (Exception error)
            {
                lock (gate)
                {
                    errors.Add(error);
                }
            }
        }

        private void Serve(Socket client)
        {
            var server = new Server(this, new BcTlsCrypto(new SecureRandom()));
            var protocol = new TlsServerProtocol();
            Socket backend = null;
            var buffer = new byte[32 * 1024];
            var toBackend = new byte[32 * 1024];
            int toBackendStart = 0;
            int toBackendEnd = 0;
            client.Blocking = false;
            try
            {
                protocol.Accept(server);
                while (!stopping)
                {
                    bool idle = true;
                    if (toBackendStart == toBackendEnd && protocol.GetAvailableInputBytes() == 0)
                    {
                        int read = client.Receive(buffer, 0, buffer.Length, SocketFlags.None, out SocketError error);
                        if (error == SocketError.Success)
                        {
                            if (read == 0)
                            {
                                break;
                            }

                            idle = false;
                            try
                            {
                                protocol.OfferInput(buffer, 0, read);
                            }
                            finally
                            {
                                Flush(protocol, client);
                            }

                            if (protocol.IsClosed)
                            {
                                lock (gate)
                                {
                                    closeNotifies++;
                                }

                                break;
                            }
                        }
                        else if (error != SocketError.WouldBlock)
                        {
                            break;
                        }
                    }

                    if (server.Completed && backend == null)
                    {
                        if (CloseAfterHandshake)
                        {
                            protocol.Close();
                            Flush(protocol, client);
                            while (KeepSocketAfterClose && !stopping)
                            {
                                Thread.Sleep(1);
                            }

                            break;
                        }

                        backend = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        backend.Connect(new IPEndPoint(IPAddress.Loopback, backendPort));
                        backend.Blocking = false;
                    }

                    if (backend == null)
                    {
                        if (idle)
                        {
                            Thread.Sleep(1);
                        }

                        continue;
                    }

                    if (toBackendStart == toBackendEnd)
                    {
                        int available = protocol.GetAvailableInputBytes();
                        if (available > 0)
                        {
                            toBackendStart = 0;
                            toBackendEnd = protocol.ReadInput(toBackend, 0, Math.Min(available, toBackend.Length));
                        }
                    }

                    if (toBackendStart < toBackendEnd && backend.Poll(0, SelectMode.SelectWrite))
                    {
                        int sent = backend.Send(toBackend, toBackendStart, toBackendEnd - toBackendStart, SocketFlags.None, out SocketError error);
                        if (error == SocketError.Success)
                        {
                            idle = false;
                            toBackendStart += sent;
                        }
                        else if (error != SocketError.WouldBlock)
                        {
                            break;
                        }
                    }

                    int fromBackend = backend.Receive(buffer, 0, buffer.Length, SocketFlags.None, out SocketError backendError);
                    if (backendError == SocketError.Success)
                    {
                        if (fromBackend == 0)
                        {
                            protocol.Close();
                            Flush(protocol, client);
                            break;
                        }

                        idle = false;
                        protocol.WriteApplicationData(buffer, 0, fromBackend);
                        Flush(protocol, client);
                    }
                    else if (backendError != SocketError.WouldBlock)
                    {
                        break;
                    }

                    if (idle)
                    {
                        Thread.Sleep(1);
                    }
                }
            }
            catch (Exception error)
            {
                lock (gate)
                {
                    errors.Add(error);
                }
            }
            finally
            {
                client.Dispose();
                backend?.Dispose();
            }
        }

        private void Flush(TlsServerProtocol protocol, Socket client)
        {
            int available = protocol.GetAvailableOutputBytes();
            if (available == 0)
            {
                return;
            }

            var output = new byte[available];
            protocol.ReadOutput(output, 0, available);
            int sent = 0;
            while (sent < available && !stopping)
            {
                if (!client.Poll(1000, SelectMode.SelectWrite))
                {
                    continue;
                }

                int count = client.Send(output, sent, available - sent, SocketFlags.None, out SocketError error);
                if (error == SocketError.WouldBlock)
                {
                    continue;
                }

                if (error != SocketError.Success)
                {
                    throw new SocketException((int)error);
                }

                sent += count;
            }
        }

        private sealed class Server : DefaultTlsServer
        {
            private readonly TlsTestProxy owner;

            public Server(TlsTestProxy owner, BcTlsCrypto crypto)
                : base(crypto)
            {
                this.owner = owner;
            }

            public bool Completed { get; private set; }

            public override void ProcessClientExtensions(IDictionary<int, byte[]> clientExtensions)
            {
                base.ProcessClientExtensions(clientExtensions);
                IList<ServerName> names = TlsExtensionsUtilities.GetServerNameExtensionClient(clientExtensions);
                lock (owner.gate)
                {
                    if (names == null)
                    {
                        owner.serverNames.Add(null);
                    }
                    else
                    {
                        foreach (ServerName name in names)
                        {
                            owner.serverNames.Add(Encoding.ASCII.GetString(name.NameData));
                        }
                    }
                }
            }

            public override int GetSelectedCipherSuite()
            {
                if (owner.RefuseHandshake)
                {
                    throw new TlsFatalAlert(AlertDescription.handshake_failure);
                }

                return base.GetSelectedCipherSuite();
            }

            public override TlsCredentials GetCredentials()
            {
                var certificates = new TlsCertificate[owner.chain.Length];
                var entries = new CertificateEntry[owner.chain.Length];
                for (int index = 0; index < owner.chain.Length; index++)
                {
                    certificates[index] = Crypto.CreateCertificate(owner.chain[index]);
                    entries[index] = new CertificateEntry(certificates[index], null);
                }

                Certificate certificate = TlsUtilities.IsTlsV13(m_context)
                    ? new Certificate(TlsUtilities.EmptyBytes, entries)
                    : new Certificate(certificates);
                return new BcDefaultTlsCredentialedSigner(
                    new TlsCryptoParameters(m_context),
                    (BcTlsCrypto)Crypto,
                    owner.privateKey,
                    certificate,
                    TlsUtilities.IsSignatureAlgorithmsExtensionAllowed(m_context.ServerVersion)
                        ? SignatureAndHashAlgorithm.GetInstance(HashAlgorithm.sha256, SignatureAlgorithm.ecdsa)
                        : null);
            }

            public override void NotifyHandshakeComplete()
            {
                base.NotifyHandshakeComplete();
                lock (owner.gate)
                {
                    owner.versions.Add(m_context.ServerVersion);
                }

                Completed = true;
            }

            protected override ProtocolVersion[] GetSupportedVersions() => owner.Versions ?? base.GetSupportedVersions();

            protected override int[] GetSupportedCipherSuites() => new[]
            {
                CipherSuite.TLS_AES_128_GCM_SHA256,
                CipherSuite.TLS_CHACHA20_POLY1305_SHA256,
                CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
                CipherSuite.TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256,
                CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA,
            };
        }
    }

    internal sealed class RawTestServer : IDisposable
    {
        private readonly Socket listener;
        private readonly Thread thread;
        private readonly List<Socket> accepted = new List<Socket>();
        private readonly byte[] reply;
        private readonly bool closeAtOnce;
        private volatile bool stopping;

        public RawTestServer(byte[] reply, bool closeAtOnce)
        {
            this.reply = reply;
            this.closeAtOnce = closeAtOnce;
            listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(16);
            thread = new Thread(AcceptLoop) { IsBackground = true };
            thread.Start();
        }

        public ushort Port => (ushort)((IPEndPoint)listener.LocalEndPoint).Port;

        public int Accepted
        {
            get
            {
                lock (accepted)
                {
                    return accepted.Count;
                }
            }
        }

        public void Dispose()
        {
            stopping = true;
            thread.Join(TimeSpan.FromSeconds(5));
            listener.Dispose();
            lock (accepted)
            {
                foreach (Socket socket in accepted)
                {
                    socket.Dispose();
                }
            }
        }

        private void AcceptLoop()
        {
            while (!stopping)
            {
                Socket socket;
                try
                {
                    if (!listener.Poll(1000, SelectMode.SelectRead))
                    {
                        continue;
                    }

                    socket = listener.Accept();
                }
                catch (Exception)
                {
                    return;
                }

                lock (accepted)
                {
                    accepted.Add(socket);
                }

                try
                {
                    if (reply != null)
                    {
                        socket.Send(reply);
                    }

                    if (closeAtOnce)
                    {
                        socket.Shutdown(SocketShutdown.Both);
                        socket.Close();
                    }
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
