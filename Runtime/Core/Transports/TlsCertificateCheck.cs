using System;
using System.Collections.Generic;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Pkix;
using Org.BouncyCastle.Utilities.Collections;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.X509.Store;
using Org.BouncyCastle.X509.Extension;

namespace Fomoxa.Networking.Transports
{
    public interface ITlsCertificateCheck
    {
        bool Accepts(string host, IReadOnlyList<byte[]> chain, out string rejection);
    }

    public sealed class TlsCertificateCheck : ITlsCertificateCheck
    {
        public TlsCertificateCheck(TlsRoots roots)
        {
            Roots = roots ?? throw new ArgumentNullException(nameof(roots));
        }

        public TlsRoots Roots { get; }

        internal Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        public bool Accepts(string host, IReadOnlyList<byte[]> chain, out string rejection)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            if (chain == null)
            {
                throw new ArgumentNullException(nameof(chain));
            }

            if (chain.Count == 0)
            {
                rejection = "the server sent no certificate";
                return false;
            }

            var parser = new X509CertificateParser();
            var certificates = new List<X509Certificate>(chain.Count);
            try
            {
                foreach (byte[] der in chain)
                {
                    certificates.Add(parser.ReadCertificate(der) ?? throw new FormatException());
                }
            }
            catch (Exception)
            {
                rejection = "the server certificate cannot be read";
                return false;
            }

            X509Certificate leaf = certificates[0];
            if (!LeadsToRoot(leaf, certificates, out rejection))
            {
                return false;
            }

            if (!AllowsServerAuthentication(leaf))
            {
                rejection = "the server certificate is not for server authentication";
                return false;
            }

            if (!TlsHostName.Matches(host, leaf))
            {
                rejection = $"the server certificate is not for {host}";
                return false;
            }

            rejection = null;
            return true;
        }

        private bool LeadsToRoot(X509Certificate leaf, List<X509Certificate> certificates, out string rejection)
        {
            var selector = new X509CertStoreSelector { Certificate = leaf };
            var parameters = new PkixBuilderParameters(Roots.Anchors, selector)
            {
                IsRevocationEnabled = false,
                Date = Clock(),
            };
            parameters.AddStoreCert(CollectionUtilities.CreateStore(certificates));
            try
            {
                new PkixCertPathBuilder().Build(parameters);
            }
            catch (Exception error)
            {
                rejection = $"the server certificate does not lead to a trusted root: {error.Message}";
                return false;
            }

            rejection = null;
            return true;
        }

        private static bool AllowsServerAuthentication(X509Certificate leaf)
        {
            IList<DerObjectIdentifier> purposes = leaf.GetExtendedKeyUsage();
            return purposes == null
                || purposes.Contains(KeyPurposeID.id_kp_serverAuth)
                || purposes.Contains(KeyPurposeID.AnyExtendedKeyUsage);
        }
    }
}
