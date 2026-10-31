using System;
using System.Buffers.Binary;
using System.Linq;
using BundleFixture;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class ConnectTokenVectorsTest
    {
        private static readonly TokenVector[] Vectors =
        {
            new TokenVector(
                key: Counting(0x01, 32),
                nonce: Counting(0xA0, 12),
                clientKey: Counting(0x40, 32),
                serverKey: Counting(0x60, 32),
                target: "pod:7",
                pod: 7,
                sessionId: 0x0102_0304_0000_0007UL,
                expires: 1_793_016_000_000L,
                data: new byte[0],
                grant: "8a00000001070000000403020100ee1478a10100000c000000a0a1a2a3a4a5a6a7a8a9aaab65000000932259f581346e5e2d9b749ab90950fff162c044c1c17d9640f63b893c53b766b5e702de3578412ae4e502657d3ce19186090e23222c3b56b8afa694697d625ea377731922e4397b692947aaf042730a0681f95e4a74c79ea0cca1751196ea64613aad50fc20000000404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f20000000606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f"),
            new TokenVector(
                key: Hex("c3f0b095a344c6d3ec55d42587cbfc1f96ed8305535b961f894a8691c3646274"),
                nonce: Counting(0xF4, 12),
                clientKey: Counting(0xC0, 32),
                serverKey: Counting(0xE0, 32),
                target: "eu-west/pod-3",
                pod: 3,
                sessionId: 0xFFFF_FFFF_0000_0003UL,
                expires: 1_793_016_030_000L,
                data: Enumerable.Range(0, 300).Select(value => (byte)value).ToArray(),
                grant: "be0100000103000000ffffffff30631578a10100000c000000f4f5f6f7f8f9fafbfcfdfeff99010000eda108c02f94120dbbd554990d6e9a7bf3690faeabaec8d942d02efb56f57ee0e4ad8b6e16440176622fbad3f7ea4bdfaf9bd108667cbba4eadfb4e04b954c39f6eebfa2e9ff75fa0c1ade793302bf51dd537921086712266afee9b0f218053a1c03cdd8e95ef34104380abea1233cb9acc8ddbe81142a9efa6b314379f1dba82351a2e9600df65319dc84ba8c20328e19743df33f6bdde01b8e14820738d85123be7d045be37d55f7e1dd8f9797fa69ff93ed15594d5a9b1490e3db7d2c83430de70647a17cf5de4d54c6cf1c6848f352c516c7c7184437dccaa91f680150d254c731dfba121d3b4f1be8b9993e797f16128553d857c1a54838d21e647c1226192b38e36def05cc6c25a49293c6c476d1a1e988fd2ce7783c415405d5ccc0da0688e3193ea35229b2dd8031bca07e6208533fa5016cc5fd30ffa8751aa5def3dd1c43f4e312f8cab3ff55714285a5d0fa74e8b644f64bde9455ae709521949cb7bb0adb01b283ce74d63b76195ff1b0d003977ab3a7cc70c2a15320538845ed475100a232feba0f512e97a9ebb84132ceb80fa39134925d0820000000c0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedf20000000e0e1e2e3e4e5e6e7e8e9eaebecedeeeff0f1f2f3f4f5f6f7f8f9fafbfcfdfeff"),
            new TokenVector(
                key: Counting(0x80, 32),
                nonce: new byte[12],
                clientKey: Enumerable.Repeat((byte)0xFF, 32).ToArray(),
                serverKey: Enumerable.Repeat((byte)0x11, 32).ToArray(),
                target: "phòng-12 🎮",
                pod: uint.MaxValue,
                sessionId: ulong.MaxValue,
                expires: 4_102_444_800_000L,
                data: System.Text.Encoding.UTF8.GetBytes("room=lobby:12"),
                grant: "a000000001ffffffffffffffff00d8c32cbb0300000c0000000000000000000000000000007b000000245c1aad4e322d79a35608ce96fd5db5f8664ab9c37a3e9758cdad59ece7f474eb74a49d21a3b0937cdba0b29f374f6f684d121a842834282fb01fce4b790938f3ae189bf1846dd80ba4f069e4b20d2904bbdb30dc6bd5499a0a80653aeea46148f3257a83302764df0a5cfd46c801bd7cf6a6808a26b05c008f5720000000ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff200000001111111111111111111111111111111111111111111111111111111111111111"),
        };

        [Test]
        public void CoreSealsEachVectorToTheBytesTheGatewayIssues()
        {
            for (int index = 0; index < Vectors.Length; index++)
            {
                TokenVector vector = Vectors[index];
                CollectionAssert.AreEqual(Hex(vector.Grant), Seal(vector), $"vector {index}");
            }
        }

        [Test]
        public void ConnectTokensOpensEachTokenTheGatewayIssued()
        {
            for (int index = 0; index < Vectors.Length; index++)
            {
                TokenVector vector = Vectors[index];
                DateTime beforeExpiry = DateTimeOffset.FromUnixTimeMilliseconds(vector.Expires - 1).UtcDateTime;
                var tokens = new ConnectTokens(TestObjects.Registry(), vector.Key, null, () => beforeExpiry);
                var grant = new ConnectionGrant();
                ConnectionGrantNetAdapter.Instance.Decode(Hex(vector.Grant), ref grant);

                Assert.IsTrue(tokens.TryOpen(grant.Token, vector.Target, out OpenedConnectToken opened), $"vector {index}");
                Assert.AreEqual(vector.SessionId, opened.SessionId, $"vector {index}");
                Assert.AreEqual(DateTimeOffset.FromUnixTimeMilliseconds(vector.Expires).UtcDateTime, opened.Expires, $"vector {index}");
                CollectionAssert.AreEqual(vector.Nonce, opened.Nonce, $"vector {index}");
                CollectionAssert.AreEqual(vector.ClientKey, opened.ClientKey, $"vector {index}");
                CollectionAssert.AreEqual(vector.ServerKey, opened.ServerKey, $"vector {index}");
                CollectionAssert.AreEqual(vector.Data, opened.Data, $"vector {index}");
                CollectionAssert.AreEqual(vector.ClientKey, grant.ClientKey.ToArray(), $"vector {index}");
                CollectionAssert.AreEqual(vector.ServerKey, grant.ServerKey.ToArray(), $"vector {index}");
            }
        }

        [Test]
        public void TheFirstFourBytesOfEveryDatagramAreThePodIdTheGatewayRoutesBy()
        {
            foreach (TokenVector vector in Vectors)
            {
                var datagram = new byte[EncryptedDatagram.DataHeaderSize];
                EncryptedDatagram.WriteDataHeader(datagram, vector.SessionId, EncryptedDatagram.DataKind, 1);

                Assert.AreEqual(vector.Pod, BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(datagram, EncryptedDatagram.SessionIdOffset, 4)));
            }
        }

        [Test]
        public void AVectorTokenDoesNotOpenForAnotherTargetOrAfterItExpires()
        {
            TokenVector vector = Vectors[0];
            DateTime atExpiry = DateTimeOffset.FromUnixTimeMilliseconds(vector.Expires).UtcDateTime;
            var expired = new ConnectTokens(TestObjects.Registry(), vector.Key, null, () => atExpiry);
            var current = new ConnectTokens(TestObjects.Registry(), vector.Key, null, () => atExpiry.AddMilliseconds(-1));
            var grant = new ConnectionGrant();
            ConnectionGrantNetAdapter.Instance.Decode(Hex(vector.Grant), ref grant);

            Assert.IsFalse(expired.TryOpen(grant.Token, vector.Target, out _));
            Assert.IsFalse(current.TryOpen(grant.Token, "pod:8", out _));
        }

        private static byte[] Seal(TokenVector vector)
        {
            var secret = new ConnectTokenSecret
            {
                Target = vector.Target,
                ClientKey = vector.ClientKey,
                ServerKey = vector.ServerKey,
                Data = vector.Data,
            };
            byte[] plain = ConnectTokenSecretNetAdapter.Instance.Encode(secret).ToArray();
            var token = new ConnectToken
            {
                Version = ConnectTokens.Version,
                SessionId = vector.SessionId,
                Expires = vector.Expires,
                Nonce = vector.Nonce,
                Sealed = ReadOnlyMemory<byte>.Empty,
            };
            token.Sealed = DatagramCipher.SealToken(vector.Key, vector.Nonce, ConnectTokenNetAdapter.Instance.Encode(token).ToArray(), plain);
            var grant = new ConnectionGrant
            {
                Token = ConnectTokenNetAdapter.Instance.Encode(token).ToArray(),
                ClientKey = vector.ClientKey,
                ServerKey = vector.ServerKey,
            };
            return ConnectionGrantNetAdapter.Instance.Encode(grant).ToArray();
        }

        private static byte[] Counting(int start, int count) =>
            Enumerable.Range(start, count).Select(value => (byte)value).ToArray();

        private static byte[] Hex(string text)
        {
            var bytes = new byte[text.Length / 2];
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] = Convert.ToByte(text.Substring(index * 2, 2), 16);
            }

            return bytes;
        }

        private sealed class TokenVector
        {
            public TokenVector(byte[] key, byte[] nonce, byte[] clientKey, byte[] serverKey, string target, uint pod, ulong sessionId, long expires, byte[] data, string grant)
            {
                Key = key;
                Nonce = nonce;
                ClientKey = clientKey;
                ServerKey = serverKey;
                Target = target;
                Pod = pod;
                SessionId = sessionId;
                Expires = expires;
                Data = data;
                Grant = grant;
            }

            public byte[] Key { get; }

            public byte[] Nonce { get; }

            public byte[] ClientKey { get; }

            public byte[] ServerKey { get; }

            public string Target { get; }

            public uint Pod { get; }

            public ulong SessionId { get; }

            public long Expires { get; }

            public byte[] Data { get; }

            public string Grant { get; }
        }
    }
}
