using System;
using System.Globalization;
using System.Net;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.X509.Extension;

namespace Fomoxa.Networking.Transports
{
    internal static class TlsHostName
    {
        public static string Normalize(string host)
        {
            string trimmed = host.EndsWith(".", StringComparison.Ordinal) ? host.Substring(0, host.Length - 1) : host;
            if (IPAddress.TryParse(trimmed, out _))
            {
                return trimmed;
            }

            for (int index = 0; index < trimmed.Length; index++)
            {
                if (trimmed[index] > 0x7F)
                {
                    return new IdnMapping().GetAscii(trimmed).ToLowerInvariant();
                }
            }

            return trimmed.ToLowerInvariant();
        }

        public static string ServerNameIndication(string host)
        {
            string normalized = Normalize(host);
            return IPAddress.TryParse(normalized, out _) ? null : normalized;
        }

        public static bool Matches(string host, X509Certificate leaf)
        {
            string normalized = Normalize(host);
            if (normalized.Length == 0)
            {
                return false;
            }

            Asn1OctetString extension = leaf.GetExtensionValue(X509Extensions.SubjectAlternativeName);
            if (extension == null)
            {
                return false;
            }

            GeneralName[] names = GeneralNames.GetInstance(X509ExtensionUtilities.FromExtensionValue(extension)).GetNames();
            bool isAddress = IPAddress.TryParse(normalized, out IPAddress address);
            foreach (GeneralName name in names)
            {
                if (isAddress
                    ? name.TagNo == GeneralName.IPAddress && MatchesAddress(address, name)
                    : name.TagNo == GeneralName.DnsName && MatchesDnsName(normalized, ((IAsn1String)name.Name).GetString()))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesAddress(IPAddress address, GeneralName name)
        {
            byte[] expected = Asn1OctetString.GetInstance(name.Name).GetOctets();
            byte[] actual = address.GetAddressBytes();
            if (expected.Length != actual.Length)
            {
                return false;
            }

            for (int index = 0; index < actual.Length; index++)
            {
                if (expected[index] != actual[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool MatchesDnsName(string host, string pattern)
        {
            string name = pattern.EndsWith(".", StringComparison.Ordinal) ? pattern.Substring(0, pattern.Length - 1) : pattern;
            name = name.ToLowerInvariant();
            if (!name.StartsWith("*.", StringComparison.Ordinal))
            {
                return name.IndexOf('*') < 0 && string.Equals(host, name, StringComparison.Ordinal);
            }

            string suffix = name.Substring(1);
            if (suffix.IndexOf('*') >= 0)
            {
                return false;
            }

            int firstDot = host.IndexOf('.');
            return firstDot > 0 && string.Equals(host.Substring(firstDot), suffix, StringComparison.Ordinal);
        }
    }
}
