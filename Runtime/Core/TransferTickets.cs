using System;
using System.Security.Cryptography;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Networking
{
    public sealed class TransferTickets
    {
        public const int NonceLength = 16;

        private const int MacLength = 32;

        private readonly IMessageCodec<TransferTicket> codec;
        private readonly byte[] key;
        private readonly Func<DateTime> utcNow;
        private readonly TransferTicket issued = new TransferTicket();
        private readonly object gate = new object();
        private TransferTicket received = new TransferTicket();
        private TimeSpan lifetime = TimeSpan.FromSeconds(30);

        public TransferTickets(FomoxaRegistry registry, byte[] key)
            : this(registry, key, null, null)
        {
        }

        public TransferTickets(FomoxaRegistry registry, byte[] key, IUsedTicketStore usedTickets, Func<DateTime> utcNow)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (key.Length == 0)
            {
                throw new ArgumentException("a ticket key needs at least one byte", nameof(key));
            }

            codec = registry.Codec<TransferTicket>()
                ?? throw new InvalidOperationException($"no {nameof(TransferTicket)} codec is registered; FomoxaAdapters.RegisterAll has not run");
            this.key = (byte[])key.Clone();
            UsedTickets = usedTickets ?? new MemoryUsedTicketStore();
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public TimeSpan Lifetime
        {
            get => lifetime;
            set
            {
                if (value <= TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "a ticket lifetime is longer than zero");
                }

                lifetime = value;
            }
        }

        public IUsedTicketStore UsedTickets { get; }

        public byte[] Issue(string target, ReadOnlyMemory<byte> data)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var nonce = new byte[NonceLength];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(nonce);
            }

            lock (gate)
            {
                return Encode(target, data, nonce);
            }
        }

        public bool TryVerify(ReadOnlyMemory<byte> ticket, string target, out ReadOnlyMemory<byte> data)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            lock (gate)
            {
                return Verify(ticket, target, out data);
            }
        }

        private byte[] Encode(string target, ReadOnlyMemory<byte> data, byte[] nonce)
        {
            issued.Data = data;
            issued.Target = target;
            issued.Expires = UnixMilliseconds(utcNow() + lifetime);
            issued.Nonce = nonce;
            issued.Mac = ReadOnlyMemory<byte>.Empty;
            try
            {
                issued.Mac = Sign(codec.Encode(issued).ToArray());
                return codec.Encode(issued).ToArray();
            }
            finally
            {
                issued.Data = default;
                issued.Nonce = default;
                issued.Mac = default;
            }
        }

        private bool Verify(ReadOnlyMemory<byte> ticket, string target, out ReadOnlyMemory<byte> data)
        {
            data = default;
            try
            {
                codec.Decode(ticket, ref received);
            }
            catch (MessageDecodeException)
            {
                return false;
            }

            if (received.Mac.Length != MacLength)
            {
                return false;
            }

            byte[] mac = received.Mac.ToArray();
            received.Mac = ReadOnlyMemory<byte>.Empty;
            if (!FixedTimeEquals(mac, Sign(codec.Encode(received).ToArray())))
            {
                return false;
            }

            DateTime now = utcNow();
            if (!string.Equals(received.Target, target, StringComparison.Ordinal)
                || received.Expires <= UnixMilliseconds(now)
                || received.Nonce.Length != NonceLength
                || !UsedTickets.TryUse(received.Nonce.Span, DateTimeOffset.FromUnixTimeMilliseconds(received.Expires).UtcDateTime, now))
            {
                return false;
            }

            data = received.Data.ToArray();
            return true;
        }

        private byte[] Sign(byte[] body)
        {
            using (var hmac = new HMACSHA256(key))
            {
                return hmac.ComputeHash(body);
            }
        }

        private static long UnixMilliseconds(DateTime utc) =>
            new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            int difference = 0;
            for (int index = 0; index < left.Length; index++)
            {
                difference |= left[index] ^ right[index];
            }

            return difference == 0;
        }
    }
}
