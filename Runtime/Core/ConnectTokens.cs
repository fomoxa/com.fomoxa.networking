using System;
using System.Security.Cryptography;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Transports;

namespace Fomoxa.Networking
{
    public sealed class ConnectTokens
    {
        public const int KeyLength = DatagramCipher.KeyLength;
        public const byte Version = 1;

        private readonly IMessageCodec<ConnectToken> tokenCodec;
        private readonly IMessageCodec<ConnectTokenSecret> secretCodec;
        private readonly IMessageCodec<ConnectionGrant> grantCodec;
        private readonly byte[] key;
        private readonly Func<DateTime> utcNow;
        private readonly object gate = new object();
        private ConnectToken token = new ConnectToken();
        private ConnectTokenSecret secret = new ConnectTokenSecret();
        private readonly ConnectionGrant grant = new ConnectionGrant();
        private TimeSpan lifetime = TimeSpan.FromSeconds(30);

        public ConnectTokens(FomoxaRegistry registry, byte[] key)
            : this(registry, key, null, null)
        {
        }

        public ConnectTokens(FomoxaRegistry registry, byte[] key, IUsedTicketStore usedAttempts, Func<DateTime> utcNow)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (key.Length != KeyLength)
            {
                throw new ArgumentException($"a connect token key is {KeyLength} bytes", nameof(key));
            }

            tokenCodec = CodecOf<ConnectToken>(registry);
            secretCodec = CodecOf<ConnectTokenSecret>(registry);
            grantCodec = CodecOf<ConnectionGrant>(registry);
            this.key = (byte[])key.Clone();
            UsedAttempts = usedAttempts ?? new MemoryUsedTicketStore();
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public TimeSpan Lifetime
        {
            get => lifetime;
            set
            {
                if (value <= TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "a connect token lifetime is longer than zero");
                }

                lifetime = value;
            }
        }

        public IUsedTicketStore UsedAttempts { get; }

        public byte[] Issue(string target, ReadOnlyMemory<byte> data) => Issue(target, NewSessionId(), data);

        public byte[] Issue(string target, ulong sessionId, ReadOnlyMemory<byte> data)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (sessionId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sessionId), "session id 0 means no session");
            }

            byte[] clientKey = RandomBytes(KeyLength);
            byte[] serverKey = RandomBytes(KeyLength);
            byte[] nonce = RandomBytes(DatagramCipher.NonceLength);
            lock (gate)
            {
                secret.Target = target;
                secret.ClientKey = clientKey;
                secret.ServerKey = serverKey;
                secret.Data = data;
                byte[] plain = secretCodec.Encode(secret).ToArray();
                secret.Data = default;

                token.Version = Version;
                token.SessionId = sessionId;
                token.Expires = UnixMilliseconds(utcNow() + lifetime);
                token.Nonce = nonce;
                token.Sealed = ReadOnlyMemory<byte>.Empty;
                token.Sealed = DatagramCipher.SealToken(key, nonce, tokenCodec.Encode(token).ToArray(), plain);
                grant.Token = tokenCodec.Encode(token).ToArray();
                token.Sealed = default;

                grant.ClientKey = clientKey;
                grant.ServerKey = serverKey;
                byte[] issued = grantCodec.Encode(grant).ToArray();
                grant.Token = default;
                return issued;
            }
        }

        internal bool TryOpen(ReadOnlyMemory<byte> bytes, string target, out OpenedConnectToken opened)
        {
            opened = default;
            lock (gate)
            {
                try
                {
                    tokenCodec.Decode(bytes, ref token);
                }
                catch (MessageDecodeException)
                {
                    return false;
                }

                if (token.Version != Version || token.Nonce.Length != DatagramCipher.NonceLength || token.SessionId == 0)
                {
                    return false;
                }

                byte[] nonce = token.Nonce.ToArray();
                byte[] sealedSecret = token.Sealed.ToArray();
                token.Sealed = ReadOnlyMemory<byte>.Empty;
                byte[] plain = DatagramCipher.OpenToken(key, nonce, tokenCodec.Encode(token).ToArray(), sealedSecret);
                if (plain == null)
                {
                    return false;
                }

                try
                {
                    secretCodec.Decode(plain, ref secret);
                }
                catch (MessageDecodeException)
                {
                    return false;
                }

                if (!string.Equals(secret.Target, target, StringComparison.Ordinal)
                    || token.Expires <= UnixMilliseconds(utcNow())
                    || secret.ClientKey.Length != KeyLength
                    || secret.ServerKey.Length != KeyLength)
                {
                    return false;
                }

                opened = new OpenedConnectToken(
                    token.SessionId,
                    DateTimeOffset.FromUnixTimeMilliseconds(token.Expires).UtcDateTime,
                    nonce,
                    secret.ClientKey.ToArray(),
                    secret.ServerKey.ToArray(),
                    secret.Data.ToArray());
                return true;
            }
        }

        internal bool TryUseAttempt(in OpenedConnectToken opened, byte[] attempt)
        {
            byte[] pair = new byte[opened.Nonce.Length + attempt.Length];
            Buffer.BlockCopy(opened.Nonce, 0, pair, 0, opened.Nonce.Length);
            Buffer.BlockCopy(attempt, 0, pair, opened.Nonce.Length, attempt.Length);
            byte[] digest;
            using (SHA256 sha = SHA256.Create())
            {
                digest = sha.ComputeHash(pair);
            }

            return UsedAttempts.TryUse(new ReadOnlySpan<byte>(digest, 0, TransferTickets.NonceLength), opened.Expires, utcNow());
        }

        internal static bool TryReadGrant(IMessageCodec<ConnectionGrant> codec, ReadOnlyMemory<byte> bytes, out ConnectionGrant read)
        {
            read = new ConnectionGrant();
            try
            {
                codec.Decode(bytes, ref read);
            }
            catch (MessageDecodeException)
            {
                return false;
            }

            return read.Token.Length > 0 && read.ClientKey.Length == KeyLength && read.ServerKey.Length == KeyLength;
        }

        internal static IMessageCodec<T> CodecOf<T>(FomoxaRegistry registry) =>
            registry.Codec<T>() ?? throw new InvalidOperationException($"no {typeof(T).Name} codec is registered; FomoxaAdapters.RegisterAll has not run");

        private static ulong NewSessionId()
        {
            while (true)
            {
                ulong candidate = BitConverter.ToUInt64(RandomBytes(8), 0);
                if (candidate != 0)
                {
                    return candidate;
                }
            }
        }

        private static byte[] RandomBytes(int count)
        {
            var bytes = new byte[count];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            return bytes;
        }

        private static long UnixMilliseconds(DateTime utc) =>
            new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
    }

    internal readonly struct OpenedConnectToken
    {
        public OpenedConnectToken(ulong sessionId, DateTime expires, byte[] nonce, byte[] clientKey, byte[] serverKey, byte[] data)
        {
            SessionId = sessionId;
            Expires = expires;
            Nonce = nonce;
            ClientKey = clientKey;
            ServerKey = serverKey;
            Data = data;
        }

        public ulong SessionId { get; }

        public DateTime Expires { get; }

        public byte[] Nonce { get; }

        public byte[] ClientKey { get; }

        public byte[] ServerKey { get; }

        public byte[] Data { get; }
    }
}
