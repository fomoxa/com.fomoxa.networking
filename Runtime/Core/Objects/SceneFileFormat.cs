using System;
using System.Buffers.Binary;
using System.IO;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Networking.Objects
{
    public static class SceneFileFormat
    {
        public const string Extension = ".fomoxascene";

        private const int HeaderSize = sizeof(ulong);

        public static byte[] Write(FomoxaRegistry registry, SceneFile file)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            IMessageCodec<SceneFile> codec = CodecOf(registry);
            ulong fingerprint = FingerprintOf(registry, codec);
            ReadOnlySpan<byte> body = codec.Encode(file).Span;
            var bytes = new byte[HeaderSize + body.Length];
            BinaryPrimitives.WriteUInt64LittleEndian(bytes, fingerprint);
            body.CopyTo(bytes.AsSpan(HeaderSize));
            return bytes;
        }

        public static SceneFile Read(FomoxaRegistry registry, ReadOnlyMemory<byte> bytes)
        {
            IMessageCodec<SceneFile> codec = CodecOf(registry);
            ulong expected = FingerprintOf(registry, codec);
            if (bytes.Length < HeaderSize)
            {
                throw new InvalidDataException($"a scene file starts with an {HeaderSize}-byte SceneFile fingerprint; this one has {bytes.Length} bytes");
            }

            ulong found = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Span);
            if (found != expected)
            {
                throw new InvalidDataException($"the scene file was written for SceneFile fingerprint 0x{found:X16}, this build reads 0x{expected:X16}; export the scene again");
            }

            var file = new SceneFile();
            codec.Decode(bytes.Slice(HeaderSize), ref file);
            return file;
        }

        private static IMessageCodec<SceneFile> CodecOf(FomoxaRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            return registry.Codec<SceneFile>()
                ?? throw new InvalidOperationException("no SceneFile codec is registered; FomoxaAdapters.RegisterAll has not run");
        }

        private static ulong FingerprintOf(FomoxaRegistry registry, IMessageCodec<SceneFile> codec)
        {
            MessageSchema message = registry.Schema?.Message(codec.MessageId)
                ?? throw new InvalidOperationException("the registry schema has no SceneFile message; FomoxaAdapters.RegisterAll has not run");
            return message.Fingerprint;
        }
    }
}
