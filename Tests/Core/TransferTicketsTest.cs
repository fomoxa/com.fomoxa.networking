using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BundleFixture;
using Fomoxa.Networking.Messaging;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class TransferTicketsTest
    {
        private static readonly byte[] Key = Encoding.UTF8.GetBytes("a key shared by the servers");

        private DateTime now;
        private TransferTickets tickets;

        [SetUp]
        public void SetUp()
        {
            now = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
            tickets = new TransferTickets(TestObjects.Registry(), Key, null, () => now);
        }

        [Test]
        public void AnIssuedTicketVerifiesAtItsTargetAndReturnsItsData()
        {
            byte[] ticket = tickets.Issue("arena-2", new byte[] { 4, 2 });

            Assert.IsTrue(tickets.TryVerify(ticket, "arena-2", out ReadOnlyMemory<byte> data));
            CollectionAssert.AreEqual(new byte[] { 4, 2 }, data.ToArray());
        }

        [Test]
        public void ATicketIsAnEncodedTransferTicketWithAMacOverItsOtherFields()
        {
            byte[] ticket = tickets.Issue("arena-2", new byte[] { 9 });
            var decoded = new TransferTicket();
            TransferTicketNetAdapter.Instance.Decode(ticket, ref decoded);

            Assert.AreEqual("arena-2", decoded.Target);
            CollectionAssert.AreEqual(new byte[] { 9 }, decoded.Data.ToArray());
            Assert.AreEqual(new DateTimeOffset(now.AddSeconds(30)).ToUnixTimeMilliseconds(), decoded.Expires);
            Assert.AreEqual(TransferTickets.NonceLength, decoded.Nonce.Length);
            Assert.AreEqual(32, decoded.Mac.Length);
        }

        [Test]
        public void EachTicketHasItsOwnNonce()
        {
            var nonces = new HashSet<string>();
            for (int index = 0; index < 20; index++)
            {
                var decoded = new TransferTicket();
                TransferTicketNetAdapter.Instance.Decode(tickets.Issue("arena-2", ReadOnlyMemory<byte>.Empty), ref decoded);
                nonces.Add(Convert.ToBase64String(decoded.Nonce.ToArray()));
            }

            Assert.AreEqual(20, nonces.Count);
        }

        [Test]
        public void ATicketVerifiesOnlyOnce()
        {
            byte[] ticket = tickets.Issue("arena-2", new byte[] { 1 });

            Assert.IsTrue(tickets.TryVerify(ticket, "arena-2", out _));
            Assert.IsFalse(tickets.TryVerify(ticket, "arena-2", out ReadOnlyMemory<byte> data));
            Assert.IsTrue(data.IsEmpty);
        }

        [Test]
        public void ATicketForAnotherTargetIsRefusedAndStaysUnused()
        {
            byte[] ticket = tickets.Issue("arena-2", new byte[] { 1 });

            Assert.IsFalse(tickets.TryVerify(ticket, "arena-3", out _));
            Assert.IsTrue(tickets.TryVerify(ticket, "arena-2", out _));
        }

        [Test]
        public void ATicketIsRefusedFromTheMomentItExpires()
        {
            tickets.Lifetime = TimeSpan.FromSeconds(10);
            byte[] early = tickets.Issue("arena-2", new byte[] { 1 });
            byte[] late = tickets.Issue("arena-2", new byte[] { 1 });

            now = now.AddSeconds(10).AddMilliseconds(-1);
            Assert.IsTrue(tickets.TryVerify(early, "arena-2", out _));
            now = now.AddMilliseconds(1);
            Assert.IsFalse(tickets.TryVerify(late, "arena-2", out _));
        }

        [Test]
        public void ATicketSignedWithAnotherKeyIsRefused()
        {
            var other = new TransferTickets(TestObjects.Registry(), Encoding.UTF8.GetBytes("another key"), null, () => now);
            byte[] ticket = other.Issue("arena-2", new byte[] { 1 });

            Assert.IsFalse(tickets.TryVerify(ticket, "arena-2", out _));
        }

        [Test]
        public void ChangingAnyByteOfATicketMakesItInvalid()
        {
            byte[] ticket = tickets.Issue("arena-2", new byte[] { 1, 2, 3 });
            for (int index = 0; index < ticket.Length; index++)
            {
                byte[] changed = (byte[])ticket.Clone();
                changed[index] ^= 0x01;
                Assert.IsFalse(tickets.TryVerify(changed, "arena-2", out _), $"byte {index}");
            }

            Assert.IsTrue(tickets.TryVerify(ticket, "arena-2", out _));
        }

        [Test]
        public void BytesThatAreNotATicketAreRefused()
        {
            Assert.IsFalse(tickets.TryVerify(ReadOnlyMemory<byte>.Empty, "arena-2", out _));
            Assert.IsFalse(tickets.TryVerify(new byte[] { 1, 2, 3 }, "arena-2", out _));
        }

        [Test]
        public void ATicketWithAWrongNonceLengthIsRefusedEvenWhenSigned()
        {
            var forged = new TransferTicket();
            TransferTicketNetAdapter.Instance.Decode(tickets.Issue("arena-2", ReadOnlyMemory<byte>.Empty), ref forged);
            forged.Nonce = new byte[4];
            forged.Mac = ReadOnlyMemory<byte>.Empty;
            byte[] body = TransferTicketNetAdapter.Instance.Encode(forged).ToArray();
            using (var hmac = new System.Security.Cryptography.HMACSHA256(Key))
            {
                forged.Mac = hmac.ComputeHash(body);
            }

            Assert.IsFalse(tickets.TryVerify(TransferTicketNetAdapter.Instance.Encode(forged).ToArray(), "arena-2", out _));
        }

        [Test]
        public void TheUsedTicketStoreDecidesWhetherANonceIsStillFree()
        {
            var store = new RecordingStore { Answer = false };
            var withStore = new TransferTickets(TestObjects.Registry(), Key, store, () => now);
            byte[] ticket = withStore.Issue("arena-2", ReadOnlyMemory<byte>.Empty);

            Assert.IsFalse(withStore.TryVerify(ticket, "arena-2", out _));
            Assert.AreSame(store, withStore.UsedTickets);
            Assert.AreEqual(1, store.Calls.Count);
            Assert.AreEqual(now.AddSeconds(30), store.Calls[0].Expires);
            Assert.AreEqual(now, store.Calls[0].Now);
            Assert.AreEqual(TransferTickets.NonceLength, store.Calls[0].Nonce.Length);
        }

        [Test]
        public void TheStoreIsNotAskedForATicketThatFailsAnotherCheck()
        {
            var store = new RecordingStore { Answer = true };
            var withStore = new TransferTickets(TestObjects.Registry(), Key, store, () => now);
            byte[] ticket = withStore.Issue("arena-2", ReadOnlyMemory<byte>.Empty);

            Assert.IsFalse(withStore.TryVerify(ticket, "arena-3", out _));
            now = now.AddMinutes(1);
            Assert.IsFalse(withStore.TryVerify(ticket, "arena-2", out _));
            CollectionAssert.IsEmpty(store.Calls);
        }

        [Test]
        public void TheMemoryStoreForgetsNoncesOnceTheirTicketsHaveExpired()
        {
            var store = new MemoryUsedTicketStore();
            byte[] first = Enumerable.Repeat((byte)1, TransferTickets.NonceLength).ToArray();
            byte[] second = Enumerable.Repeat((byte)2, TransferTickets.NonceLength).ToArray();

            Assert.IsTrue(store.TryUse(first, now.AddSeconds(5), now));
            Assert.IsFalse(store.TryUse(first, now.AddSeconds(5), now.AddSeconds(4)));
            Assert.IsTrue(store.TryUse(second, now.AddSeconds(30), now.AddSeconds(4)));
            Assert.AreEqual(2, store.Count);
            Assert.IsTrue(store.TryUse(first, now.AddSeconds(40), now.AddSeconds(5)));
            Assert.AreEqual(2, store.Count);
            Assert.Throws<ArgumentException>(() => store.TryUse(new byte[3], now, now));
        }

        [Test]
        public void ArgumentsAreChecked()
        {
            Assert.Throws<ArgumentNullException>(() => new TransferTickets(null, Key));
            Assert.Throws<ArgumentNullException>(() => new TransferTickets(TestObjects.Registry(), null));
            Assert.Throws<ArgumentException>(() => new TransferTickets(TestObjects.Registry(), Array.Empty<byte>()));
            Assert.Throws<InvalidOperationException>(() => new TransferTickets(new FomoxaRegistry(), Key));
            Assert.Throws<ArgumentNullException>(() => tickets.Issue(null, ReadOnlyMemory<byte>.Empty));
            Assert.Throws<ArgumentNullException>(() => tickets.TryVerify(new byte[1], null, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => tickets.Lifetime = TimeSpan.Zero);
            Assert.AreEqual(TimeSpan.FromSeconds(30), tickets.Lifetime);
        }

        [Test]
        public void TheKeyIsCopiedWhenTheTicketsAreCreated()
        {
            byte[] key = (byte[])Key.Clone();
            var copied = new TransferTickets(TestObjects.Registry(), key, null, () => now);
            byte[] ticket = copied.Issue("arena-2", ReadOnlyMemory<byte>.Empty);
            key[0] ^= 0xFF;

            Assert.IsTrue(copied.TryVerify(ticket, "arena-2", out _));
        }

        private sealed class RecordingStore : IUsedTicketStore
        {
            public bool Answer { get; set; }

            public List<(byte[] Nonce, DateTime Expires, DateTime Now)> Calls { get; } = new List<(byte[] Nonce, DateTime Expires, DateTime Now)>();

            public bool TryUse(ReadOnlySpan<byte> nonce, DateTime expiresUtc, DateTime nowUtc)
            {
                Calls.Add((nonce.ToArray(), expiresUtc, nowUtc));
                return Answer;
            }
        }
    }
}
