using System;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace Fomoxa.Networking.Transports
{
    internal sealed class DatagramCipher
    {
        public const int KeyLength = 32;
        public const int NonceLength = 12;
        public const int TagLength = 16;

        private static readonly byte[] ClientToServer = Encoding.ASCII.GetBytes("fomoxa udp client to server");
        private static readonly byte[] ServerToClient = Encoding.ASCII.GetBytes("fomoxa udp server to client");

        private readonly ChaCha20Poly1305 aead = new ChaCha20Poly1305();
        private readonly byte[] nonce = new byte[NonceLength];

        public static byte[] DeriveClientKey(byte[] tokenKey, byte[] attempt) => Derive(tokenKey, attempt, ClientToServer);

        public static byte[] DeriveServerKey(byte[] tokenKey, byte[] attempt) => Derive(tokenKey, attempt, ServerToClient);

        public int Seal(KeyParameter key, ulong sequence, byte[] associated, int associatedLength, byte[] plain, int plainOffset, int plainLength, byte[] output, int outputOffset)
        {
            WriteNonce(sequence);
            aead.Init(true, new AeadParameters(key, TagLength * 8, nonce));
            aead.ProcessAadBytes(associated, 0, associatedLength);
            int written = aead.ProcessBytes(plain, plainOffset, plainLength, output, outputOffset);
            return written + aead.DoFinal(output, outputOffset + written);
        }

        public bool TryOpen(KeyParameter key, ulong sequence, byte[] associated, int associatedLength, byte[] sealedBytes, int sealedOffset, int sealedLength, byte[] output, int outputOffset, out int length)
        {
            length = 0;
            if (sealedLength < TagLength)
            {
                return false;
            }

            WriteNonce(sequence);
            try
            {
                aead.Init(false, new AeadParameters(key, TagLength * 8, nonce));
                aead.ProcessAadBytes(associated, 0, associatedLength);
                int written = aead.ProcessBytes(sealedBytes, sealedOffset, sealedLength, output, outputOffset);
                length = written + aead.DoFinal(output, outputOffset + written);
                return true;
            }
            catch (InvalidCipherTextException)
            {
                return false;
            }
        }

        public static byte[] SealToken(byte[] key, byte[] tokenNonce, byte[] associated, byte[] plain)
        {
            var cipher = new ChaCha20Poly1305();
            cipher.Init(true, new AeadParameters(new KeyParameter(key), TagLength * 8, tokenNonce, associated));
            var output = new byte[cipher.GetOutputSize(plain.Length)];
            int written = cipher.ProcessBytes(plain, 0, plain.Length, output, 0);
            cipher.DoFinal(output, written);
            return output;
        }

        public static byte[] OpenToken(byte[] key, byte[] tokenNonce, byte[] associated, byte[] sealedBytes)
        {
            var cipher = new ChaCha20Poly1305();
            try
            {
                cipher.Init(false, new AeadParameters(new KeyParameter(key), TagLength * 8, tokenNonce, associated));
                var output = new byte[cipher.GetOutputSize(sealedBytes.Length)];
                int written = cipher.ProcessBytes(sealedBytes, 0, sealedBytes.Length, output, 0);
                cipher.DoFinal(output, written);
                return output;
            }
            catch (InvalidCipherTextException)
            {
                return null;
            }
        }

        private static byte[] Derive(byte[] tokenKey, byte[] attempt, byte[] info)
        {
            var hkdf = new HkdfBytesGenerator(new Sha256Digest());
            hkdf.Init(new HkdfParameters(tokenKey, attempt, info));
            var key = new byte[KeyLength];
            hkdf.GenerateBytes(key, 0, KeyLength);
            return key;
        }

        private void WriteNonce(ulong sequence)
        {
            nonce[0] = 0;
            nonce[1] = 0;
            nonce[2] = 0;
            nonce[3] = 0;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(new Span<byte>(nonce, 4, 8), sequence);
        }
    }
}
