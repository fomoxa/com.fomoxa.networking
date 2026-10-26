using System;
using System.Collections.Generic;
using Fomoxa.Networking.Transports;
using NUnit.Framework;
using Org.BouncyCastle.Asn1.X509;

namespace Fomoxa.Networking.Tests
{
    public sealed class TlsCertificateCheckTest
    {
        private static readonly TestCertificate Root = TestCertificates.Root("Test Root");
        private static readonly TestCertificate Middle = TestCertificates.Intermediate(Root, "Test Intermediate");

        private static TlsCertificateCheck CheckWith(TestCertificate root) => new TlsCertificateCheck(TlsRoots.FromPem(root.Pem));

        private static IReadOnlyList<byte[]> Chain(params TestCertificate[] certificates)
        {
            var chain = new byte[certificates.Length][];
            for (int index = 0; index < certificates.Length; index++)
            {
                chain[index] = certificates[index].Der;
            }

            return chain;
        }

        private static bool Accepts(string host, LeafSpec spec)
        {
            TestCertificate leaf = TestCertificates.Leaf(Middle, spec);
            return CheckWith(Root).Accepts(host, Chain(leaf, Middle), out _);
        }

        [Test]
        public void AChainThroughAnIntermediateToATrustedRootIsAccepted()
        {
            TestCertificate leaf = TestCertificates.Leaf(Middle);

            Assert.IsTrue(CheckWith(Root).Accepts("localhost", Chain(leaf, Middle), out string rejection));
            Assert.IsNull(rejection);
        }

        [Test]
        public void AChainToAnotherRootOrWithoutItsIntermediateIsRejected()
        {
            TestCertificate leaf = TestCertificates.Leaf(Middle);
            TestCertificate other = TestCertificates.Root("Other Root");

            Assert.IsFalse(CheckWith(other).Accepts("localhost", Chain(leaf, Middle), out string untrusted));
            StringAssert.StartsWith("the server certificate does not lead to a trusted root", untrusted);
            Assert.IsFalse(CheckWith(Root).Accepts("localhost", Chain(leaf), out string incomplete));
            StringAssert.StartsWith("the server certificate does not lead to a trusted root", incomplete);
        }

        [Test]
        public void ASelfSignedLeafIsRejectedUnlessItIsARoot()
        {
            TestCertificate selfSigned = TestCertificates.Root("localhost");

            Assert.IsFalse(CheckWith(Root).Accepts("localhost", Chain(selfSigned), out _));
        }

        [Test]
        public void ALeafOutsideItsValidityIsRejected()
        {
            Assert.IsFalse(Accepts("localhost", new LeafSpec { NotBefore = DateTime.UtcNow.AddDays(-3), NotAfter = DateTime.UtcNow.AddDays(-1) }));
            Assert.IsFalse(Accepts("localhost", new LeafSpec { NotBefore = DateTime.UtcNow.AddDays(1), NotAfter = DateTime.UtcNow.AddDays(3) }));
        }

        [Test]
        public void TheCheckUsesItsClockForValidity()
        {
            TestCertificate leaf = TestCertificates.Leaf(Middle, new LeafSpec { NotBefore = DateTime.UtcNow.AddDays(-3), NotAfter = DateTime.UtcNow.AddDays(-1) });
            TlsCertificateCheck check = CheckWith(Root);
            check.Clock = () => DateTime.UtcNow.AddDays(-2);

            Assert.IsTrue(check.Accepts("localhost", Chain(leaf, Middle), out _));
        }

        [Test]
        public void ALeafWithPurposesNeedsServerAuthentication()
        {
            Assert.IsTrue(Accepts("localhost", new LeafSpec { Purposes = null }));
            Assert.IsTrue(Accepts("localhost", new LeafSpec { Purposes = new[] { KeyPurposeID.id_kp_clientAuth, KeyPurposeID.id_kp_serverAuth } }));
            Assert.IsTrue(Accepts("localhost", new LeafSpec { Purposes = new[] { KeyPurposeID.AnyExtendedKeyUsage } }));

            TestCertificate clientOnly = TestCertificates.Leaf(Middle, new LeafSpec { Purposes = new[] { KeyPurposeID.id_kp_clientAuth } });
            Assert.IsFalse(CheckWith(Root).Accepts("localhost", Chain(clientOnly, Middle), out string rejection));
            Assert.AreEqual("the server certificate is not for server authentication", rejection);
        }

        [Test]
        public void ADnsNameMatchesIgnoringCaseAndATrailingDot()
        {
            var spec = new LeafSpec { DnsNames = new[] { "Game.Example.com" }, Addresses = new string[0] };

            Assert.IsTrue(Accepts("game.example.com", spec));
            Assert.IsTrue(Accepts("GAME.example.COM.", spec));
            Assert.IsFalse(Accepts("example.com", spec));
            Assert.IsFalse(Accepts("other.example.com", spec));

            TestCertificate leaf = TestCertificates.Leaf(Middle, spec);
            Assert.IsFalse(CheckWith(Root).Accepts("other.example.com", Chain(leaf, Middle), out string rejection));
            Assert.AreEqual("the server certificate is not for other.example.com", rejection);
        }

        [Test]
        public void AWildcardMatchesExactlyTheLeftMostLabel()
        {
            var spec = new LeafSpec { DnsNames = new[] { "*.example.com" }, Addresses = new string[0] };

            Assert.IsTrue(Accepts("eu.example.com", spec));
            Assert.IsFalse(Accepts("example.com", spec));
            Assert.IsFalse(Accepts("a.eu.example.com", spec));
            Assert.IsFalse(Accepts(".example.com", spec));
        }

        [Test]
        public void AWildcardOutsideTheLeftMostLabelOrInsideALabelNeverMatches()
        {
            Assert.IsFalse(Accepts("eu.game.example.com", new LeafSpec { DnsNames = new[] { "eu.*.example.com" }, Addresses = new string[0] }));
            Assert.IsFalse(Accepts("eu1.example.com", new LeafSpec { DnsNames = new[] { "eu*.example.com" }, Addresses = new string[0] }));
            Assert.IsFalse(Accepts("a.b.example.com", new LeafSpec { DnsNames = new[] { "*.*.example.com" }, Addresses = new string[0] }));
        }

        [Test]
        public void AnAddressMatchesOnlyAddressEntries()
        {
            Assert.IsTrue(Accepts("127.0.0.1", new LeafSpec { DnsNames = new string[0], Addresses = new[] { "127.0.0.1" } }));
            Assert.IsFalse(Accepts("127.0.0.2", new LeafSpec { DnsNames = new string[0], Addresses = new[] { "127.0.0.1" } }));
            Assert.IsFalse(Accepts("127.0.0.1", new LeafSpec { DnsNames = new[] { "127.0.0.1" }, Addresses = new string[0] }));
            Assert.IsFalse(Accepts("localhost", new LeafSpec { DnsNames = new string[0], Addresses = new[] { "127.0.0.1" } }));
            Assert.IsTrue(Accepts("::1", new LeafSpec { DnsNames = new string[0], Addresses = new[] { "::1" } }));
            Assert.IsFalse(Accepts("::1", new LeafSpec { DnsNames = new string[0], Addresses = new[] { "127.0.0.1" } }));
        }

        [Test]
        public void TheCommonNameIsNotAHostName()
        {
            Assert.IsFalse(Accepts("localhost", new LeafSpec { Subject = "localhost", DnsNames = new string[0], Addresses = new string[0] }));
        }

        [Test]
        public void AnInternationalHostIsComparedInItsAsciiForm()
        {
            Assert.IsTrue(Accepts("bücher.example", new LeafSpec { DnsNames = new[] { "xn--bcher-kva.example" }, Addresses = new string[0] }));
        }

        [Test]
        public void ServerNameIndicationCarriesNamesButNotAddresses()
        {
            Assert.AreEqual("game.example.com", TlsHostName.ServerNameIndication("Game.Example.com."));
            Assert.AreEqual("xn--bcher-kva.example", TlsHostName.ServerNameIndication("bücher.example"));
            Assert.IsNull(TlsHostName.ServerNameIndication("127.0.0.1"));
            Assert.IsNull(TlsHostName.ServerNameIndication("::1"));
        }

        [Test]
        public void AnEmptyOrUnreadableChainIsRejected()
        {
            Assert.IsFalse(CheckWith(Root).Accepts("localhost", new byte[0][], out string empty));
            Assert.AreEqual("the server sent no certificate", empty);
            Assert.IsFalse(CheckWith(Root).Accepts("localhost", new[] { new byte[] { 1, 2, 3 } }, out string unreadable));
            Assert.AreEqual("the server certificate cannot be read", unreadable);
            Assert.Throws<ArgumentNullException>(() => CheckWith(Root).Accepts(null, new byte[0][], out _));
            Assert.Throws<ArgumentNullException>(() => CheckWith(Root).Accepts("localhost", null, out _));
            Assert.Throws<ArgumentNullException>(() => new TlsCertificateCheck(null));
        }

        [Test]
        public void RootsCanBeAddedToOrReplaceTheMozillaSet()
        {
            TestCertificate leaf = TestCertificates.Leaf(Middle);

            Assert.IsFalse(new TlsCertificateCheck(TlsRoots.Mozilla).Accepts("localhost", Chain(leaf, Middle), out _));
            TlsRoots joined = TlsRoots.Mozilla.With(TlsRoots.FromPem(Root.Pem));
            Assert.IsTrue(new TlsCertificateCheck(joined).Accepts("localhost", Chain(leaf, Middle), out _));
            Assert.AreEqual(TlsRoots.Mozilla.Count + 1, joined.Count);
            Assert.AreEqual(TlsRoots.Mozilla.Count, TlsRoots.Mozilla.With(TlsRoots.Mozilla).Count);
            Assert.AreEqual(2, TlsRoots.FromPem("first\n" + Root.Pem + "between\n" + Middle.Pem).Count);
        }

        [Test]
        public void PemWithoutACertificateOrWithBrokenBase64IsRefused()
        {
            Assert.Throws<ArgumentNullException>(() => TlsRoots.FromPem(null));
            Assert.Throws<ArgumentException>(() => TlsRoots.FromPem("nothing here"));
            Assert.Throws<ArgumentException>(() => TlsRoots.FromPem("-----BEGIN CERTIFICATE-----\n!!!\n-----END CERTIFICATE-----"));
            Assert.Throws<ArgumentException>(() => TlsRoots.FromPem("-----BEGIN CERTIFICATE-----\nAAAA\n"));
            Assert.Throws<ArgumentException>(() => TlsRoots.FromPem("-----BEGIN CERTIFICATE-----\nAAECAw==\n-----END CERTIFICATE-----"));
            Assert.Throws<ArgumentNullException>(() => TlsRoots.Mozilla.With(null));
        }

        [Test]
        public void TheMozillaSetChecksARealPublicChain()
        {
            Assert.AreEqual(MozillaRoots.Count, TlsRoots.Mozilla.Count);
            Assert.Greater(TlsRoots.Mozilla.Count, 100);
            var check = new TlsCertificateCheck(TlsRoots.Mozilla) { Clock = () => DateTime.Parse(GitHubChain.CapturedOn + "T00:00:00Z").ToUniversalTime() };
            byte[][] chain =
            {
                Convert.FromBase64String(GitHubChain.Leaf),
                Convert.FromBase64String(GitHubChain.Intermediate),
                Convert.FromBase64String(GitHubChain.CrossSignedRoot),
            };

            Assert.IsTrue(check.Accepts("github.com", chain, out string rejection), rejection);
            Assert.IsTrue(check.Accepts("www.github.com", chain, out _));
            Assert.IsFalse(check.Accepts("api.github.com", chain, out _));
            check.Clock = () => new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Assert.IsFalse(check.Accepts("github.com", chain, out _));
        }
    }
}
