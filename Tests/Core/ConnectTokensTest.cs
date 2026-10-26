using System;
using System.Collections.Generic;
using System.Linq;
using BundleFixture;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class ConnectTokensTest
    {
        private static readonly byte[] Key = Enumerable.Range(1, ConnectTokens.KeyLength).Select(value => (byte)value).ToArray();

        private DateTime now;
        private ConnectTokens tokens;

        [SetUp]
        public void SetUp()
        {
            now = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
            tokens = new ConnectTokens(TestObjects.Registry(), Key, null, () => now);
        }

        [Test]
        public void AGrantCarriesTheTokenAndTheTwoKeysTheTokenSeals()
        {
            ConnectionGrant grant = Grant(tokens.Issue("pod-a", 77, new byte[] { 4, 2 }));

            Assert.IsTrue(tokens.TryOpen(grant.Token, "pod-a", out OpenedConnectToken opened));
            Assert.AreEqual(77UL, opened.SessionId);
            CollectionAssert.AreEqual(grant.ClientKey.ToArray(), opened.ClientKey);
            CollectionAssert.AreEqual(grant.ServerKey.ToArray(), opened.ServerKey);
            CollectionAssert.AreEqual(new byte[] { 4, 2 }, opened.Data);
            Assert.AreEqual(now.AddSeconds(30), opened.Expires);
        }

        [Test]
        public void TheClearPartHoldsTheSessionIdExpiryAndNonceAndNoKey()
        {
            ConnectionGrant grant = Grant(tokens.Issue("pod-a", 77, new byte[] { 4, 2 }));
            var token = new ConnectToken();
            ConnectTokenNetAdapter.Instance.Decode(grant.Token, ref token);

            Assert.AreEqual(ConnectTokens.Version, token.Version);
            Assert.AreEqual(1, token.Version);
            Assert.AreEqual(77UL, token.SessionId);
            Assert.AreEqual(new DateTimeOffset(now.AddSeconds(30)).ToUnixTimeMilliseconds(), token.Expires);
            Assert.AreEqual(12, token.Nonce.Length);
            Assert.IsFalse(Contains(token.Sealed.ToArray(), grant.ClientKey.ToArray()));
            Assert.IsFalse(Contains(grant.Token.ToArray(), grant.ServerKey.ToArray()));
        }

        [Test]
        public void IssueWithoutASessionIdPicksANonZeroOne()
        {
            var ids = new HashSet<ulong>();
            for (int index = 0; index < 20; index++)
            {
                Assert.IsTrue(tokens.TryOpen(Grant(tokens.Issue("pod-a", ReadOnlyMemory<byte>.Empty)).Token, "pod-a", out OpenedConnectToken opened));
                Assert.AreNotEqual(0UL, opened.SessionId);
                ids.Add(opened.SessionId);
            }

            Assert.AreEqual(20, ids.Count);
        }

        [Test]
        public void ATokenForAnotherTargetDoesNotOpen()
        {
            ConnectionGrant grant = Grant(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty));

            Assert.IsFalse(tokens.TryOpen(grant.Token, "pod-b", out _));
        }

        [Test]
        public void AnExpiredTokenDoesNotOpen()
        {
            ConnectionGrant grant = Grant(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty));
            now = now.AddSeconds(30);

            Assert.IsFalse(tokens.TryOpen(grant.Token, "pod-a", out _));
        }

        [Test]
        public void ATokenOpensAgainForEveryAttempt()
        {
            ConnectionGrant grant = Grant(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty));

            Assert.IsTrue(tokens.TryOpen(grant.Token, "pod-a", out _));
            Assert.IsTrue(tokens.TryOpen(grant.Token, "pod-a", out _));
        }

        [Test]
        public void EachAttemptOfATokenIsUsedOnce()
        {
            ConnectionGrant grant = Grant(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty));
            Assert.IsTrue(tokens.TryOpen(grant.Token, "pod-a", out OpenedConnectToken opened));
            byte[] first = Enumerable.Repeat((byte)1, 16).ToArray();
            byte[] second = Enumerable.Repeat((byte)2, 16).ToArray();

            Assert.IsTrue(tokens.TryUseAttempt(opened, first));
            Assert.IsFalse(tokens.TryUseAttempt(opened, first));
            Assert.IsTrue(tokens.TryUseAttempt(opened, second));
        }

        [Test]
        public void AnAttemptIsForgottenWhenItsTokenExpires()
        {
            ConnectionGrant grant = Grant(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty));
            Assert.IsTrue(tokens.TryOpen(grant.Token, "pod-a", out OpenedConnectToken opened));
            Assert.IsTrue(tokens.TryUseAttempt(opened, new byte[16]));

            now = now.AddSeconds(31);
            Assert.IsTrue(tokens.TryUseAttempt(new OpenedConnectToken(78, now.AddSeconds(1), new byte[12], null, null, null), new byte[16]));
            Assert.AreEqual(1, ((MemoryUsedTicketStore)tokens.UsedAttempts).Count);
        }

        [Test]
        public void AChangedClearFieldBreaksTheSeal()
        {
            ConnectionGrant grant = Grant(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty));
            var token = new ConnectToken();
            ConnectTokenNetAdapter.Instance.Decode(grant.Token, ref token);
            token.Expires += 60000;
            byte[] extended = ConnectTokenNetAdapter.Instance.Encode(token).ToArray();
            token.Expires -= 60000;
            token.SessionId = 78;
            byte[] moved = ConnectTokenNetAdapter.Instance.Encode(token).ToArray();

            Assert.IsFalse(tokens.TryOpen(extended, "pod-a", out _));
            Assert.IsFalse(tokens.TryOpen(moved, "pod-a", out _));
        }

        [Test]
        public void ATokenOfAnotherVersionDoesNotOpen()
        {
            Assert.IsTrue(tokens.TryOpen(Sealed(ConnectTokens.Version, 77, ConnectTokens.KeyLength, ConnectTokens.KeyLength), "pod-a", out _));
            Assert.IsFalse(tokens.TryOpen(Sealed(2, 77, ConnectTokens.KeyLength, ConnectTokens.KeyLength), "pod-a", out _));
        }

        [Test]
        public void ATokenWithSessionIdZeroDoesNotOpen()
        {
            Assert.IsFalse(tokens.TryOpen(Sealed(ConnectTokens.Version, 0, ConnectTokens.KeyLength, ConnectTokens.KeyLength), "pod-a", out _));
        }

        [Test]
        public void ATokenWhoseSessionKeysAreNotThirtyTwoBytesDoesNotOpen()
        {
            Assert.IsFalse(tokens.TryOpen(Sealed(ConnectTokens.Version, 77, 16, ConnectTokens.KeyLength), "pod-a", out _));
            Assert.IsFalse(tokens.TryOpen(Sealed(ConnectTokens.Version, 77, ConnectTokens.KeyLength, 48), "pod-a", out _));
        }

        [Test]
        public void TheTwoSessionKeysOfAGrantDiffer()
        {
            ConnectionGrant grant = Grant(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty));

            CollectionAssert.AreNotEqual(grant.ClientKey.ToArray(), grant.ServerKey.ToArray());
        }

        [Test]
        public void TheTokensKeepTheirOwnCopyOfTheKey()
        {
            byte[] key = Key.ToArray();
            var own = new ConnectTokens(TestObjects.Registry(), key, null, () => now);
            byte[] issued = own.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty);
            key[0] ^= 0xFF;

            Assert.IsTrue(own.TryOpen(Grant(issued).Token, "pod-a", out _));
            Assert.IsTrue(own.TryOpen(Grant(own.Issue("pod-a", 78, ReadOnlyMemory<byte>.Empty)).Token, "pod-a", out _));
            Assert.IsTrue(tokens.TryOpen(Grant(own.Issue("pod-a", 79, ReadOnlyMemory<byte>.Empty)).Token, "pod-a", out _));
        }

        [Test]
        public void ATokenSealedWithAnotherKeyDoesNotOpen()
        {
            byte[] otherKey = Key.Reverse().ToArray();
            var other = new ConnectTokens(TestObjects.Registry(), otherKey, null, () => now);

            Assert.IsFalse(tokens.TryOpen(Grant(other.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty)).Token, "pod-a", out _));
        }

        [Test]
        public void BytesThatAreNotATokenDoNotOpen()
        {
            Assert.IsFalse(tokens.TryOpen(new byte[] { 1, 2, 3 }, "pod-a", out _));
            Assert.IsFalse(tokens.TryOpen(ReadOnlyMemory<byte>.Empty, "pod-a", out _));
        }

        [Test]
        public void TheKeyMustBeThirtyTwoBytes()
        {
            Assert.Throws<ArgumentException>(() => new ConnectTokens(TestObjects.Registry(), new byte[16]));
            Assert.Throws<ArgumentNullException>(() => new ConnectTokens(TestObjects.Registry(), null));
            Assert.Throws<ArgumentNullException>(() => new ConnectTokens(null, Key));
        }

        [Test]
        public void IssueRefusesANullTargetAndSessionIdZero()
        {
            Assert.Throws<ArgumentNullException>(() => tokens.Issue(null, 77, ReadOnlyMemory<byte>.Empty));
            Assert.Throws<ArgumentOutOfRangeException>(() => tokens.Issue("pod-a", 0, ReadOnlyMemory<byte>.Empty));
        }

        [Test]
        public void LifetimeMustBePositive()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => tokens.Lifetime = TimeSpan.Zero);
            tokens.Lifetime = TimeSpan.FromSeconds(5);
            Assert.IsTrue(tokens.TryOpen(Grant(tokens.Issue("pod-a", 77, ReadOnlyMemory<byte>.Empty)).Token, "pod-a", out OpenedConnectToken opened));
            Assert.AreEqual(now.AddSeconds(5), opened.Expires);
        }

        private byte[] Sealed(byte version, ulong sessionId, int clientKeyLength, int serverKeyLength)
        {
            var secret = new ConnectTokenSecret { Target = "pod-a", ClientKey = new byte[clientKeyLength], ServerKey = new byte[serverKeyLength] };
            var token = new ConnectToken
            {
                Version = version,
                SessionId = sessionId,
                Expires = new DateTimeOffset(now.AddSeconds(30)).ToUnixTimeMilliseconds(),
                Nonce = Enumerable.Repeat((byte)9, DatagramCipher.NonceLength).ToArray(),
            };
            byte[] associated = ConnectTokenNetAdapter.Instance.Encode(token).ToArray();
            token.Sealed = DatagramCipher.SealToken(Key, token.Nonce.ToArray(), associated, ConnectTokenSecretNetAdapter.Instance.Encode(secret).ToArray());
            return ConnectTokenNetAdapter.Instance.Encode(token).ToArray();
        }

        private static ConnectionGrant Grant(byte[] issued)
        {
            var grant = new ConnectionGrant();
            ConnectionGrantNetAdapter.Instance.Decode(issued, ref grant);
            return grant;
        }

        private static bool Contains(byte[] haystack, byte[] needle)
        {
            for (int start = 0; start + needle.Length <= haystack.Length; start++)
            {
                if (haystack.Skip(start).Take(needle.Length).SequenceEqual(needle))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
