using System;
using System.Collections.Generic;
using Org.BouncyCastle.Pkix;
using Org.BouncyCastle.X509;

namespace Fomoxa.Networking.Transports
{
    public sealed class TlsRoots
    {
        private const string Begin = "-----BEGIN CERTIFICATE-----";
        private const string End = "-----END CERTIFICATE-----";

        private readonly Lazy<IReadOnlyList<X509Certificate>> certificates;
        private readonly Lazy<HashSet<TrustAnchor>> anchors;

        private TlsRoots(Func<IReadOnlyList<X509Certificate>> load)
        {
            certificates = new Lazy<IReadOnlyList<X509Certificate>>(load);
            anchors = new Lazy<HashSet<TrustAnchor>>(() =>
            {
                var built = new HashSet<TrustAnchor>();
                foreach (X509Certificate certificate in certificates.Value)
                {
                    built.Add(new TrustAnchor(certificate, null));
                }

                return built;
            });
        }

        public static TlsRoots Mozilla { get; } = new TlsRoots(() => Read(MozillaRoots.Pem));

        public static string MozillaDate => MozillaRoots.Date;

        public int Count => certificates.Value.Count;

        public static TlsRoots FromPem(string pem)
        {
            if (pem == null)
            {
                throw new ArgumentNullException(nameof(pem));
            }

            IReadOnlyList<X509Certificate> read = Read(pem);
            if (read.Count == 0)
            {
                throw new ArgumentException("the PEM text holds no certificate", nameof(pem));
            }

            return new TlsRoots(() => read);
        }

        public TlsRoots With(TlsRoots other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            return new TlsRoots(() =>
            {
                var joined = new List<X509Certificate>(certificates.Value);
                foreach (X509Certificate certificate in other.certificates.Value)
                {
                    if (!joined.Contains(certificate))
                    {
                        joined.Add(certificate);
                    }
                }

                return joined;
            });
        }

        internal ISet<TrustAnchor> Anchors => anchors.Value;

        private static IReadOnlyList<X509Certificate> Read(string pem)
        {
            var parser = new X509CertificateParser();
            var read = new List<X509Certificate>();
            int at = 0;
            while (true)
            {
                int begin = pem.IndexOf(Begin, at, StringComparison.Ordinal);
                if (begin < 0)
                {
                    return read;
                }

                int end = pem.IndexOf(End, begin, StringComparison.Ordinal);
                if (end < 0)
                {
                    throw new ArgumentException("a PEM certificate has no END line", nameof(pem));
                }

                string body = pem.Substring(begin + Begin.Length, end - begin - Begin.Length);
                byte[] der;
                try
                {
                    der = Convert.FromBase64String(body);
                }
                catch (FormatException error)
                {
                    throw new ArgumentException("a PEM certificate is not valid base64", nameof(pem), error);
                }

                read.Add(ReadDer(parser, der));
                at = end + End.Length;
            }
        }

        private static X509Certificate ReadDer(X509CertificateParser parser, byte[] der)
        {
            try
            {
                return parser.ReadCertificate(der) ?? throw new ArgumentException("a PEM certificate cannot be read", "pem");
            }
            catch (Exception error) when (!(error is ArgumentException))
            {
                throw new ArgumentException("a PEM certificate cannot be read", "pem", error);
            }
        }
    }
}
