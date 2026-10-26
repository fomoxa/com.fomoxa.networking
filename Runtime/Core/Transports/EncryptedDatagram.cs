using System;
using System.Buffers.Binary;

namespace Fomoxa.Networking.Transports
{
    internal static class EncryptedDatagram
    {
        public const byte ConnectKind = 0;
        public const byte DataKind = 1;
        public const int SessionIdOffset = 0;
        public const int KindOffset = 8;
        public const int SequenceOffset = 9;
        public const int DataHeaderSize = 17;
        public const int TokenLengthSize = 2;
        public const int AttemptSize = 16;
        public const int MaxDatagram = 65507;
        public const int DataOverhead = DataHeaderSize + DatagramCipher.TagLength;

        public static int ConnectHeaderSize(int tokenLength) => DataHeaderSize + TokenLengthSize + tokenLength + AttemptSize;

        public static void WriteDataHeader(byte[] datagram, ulong sessionId, byte kind, ulong sequence)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(new Span<byte>(datagram, SessionIdOffset, 8), sessionId);
            datagram[KindOffset] = kind;
            BinaryPrimitives.WriteUInt64LittleEndian(new Span<byte>(datagram, SequenceOffset, 8), sequence);
        }

        public static void WriteConnectHeader(byte[] datagram, ulong sessionId, ulong sequence, byte[] token, byte[] attempt)
        {
            WriteDataHeader(datagram, sessionId, ConnectKind, sequence);
            BinaryPrimitives.WriteUInt16LittleEndian(new Span<byte>(datagram, DataHeaderSize, TokenLengthSize), (ushort)token.Length);
            Buffer.BlockCopy(token, 0, datagram, DataHeaderSize + TokenLengthSize, token.Length);
            Buffer.BlockCopy(attempt, 0, datagram, DataHeaderSize + TokenLengthSize + token.Length, AttemptSize);
        }

        public static ulong ReadSessionId(byte[] datagram) =>
            BinaryPrimitives.ReadUInt64LittleEndian(new ReadOnlySpan<byte>(datagram, SessionIdOffset, 8));

        public static ulong ReadSequence(byte[] datagram) =>
            BinaryPrimitives.ReadUInt64LittleEndian(new ReadOnlySpan<byte>(datagram, SequenceOffset, 8));

        public static bool TryReadConnect(byte[] datagram, int count, out int tokenOffset, out int tokenLength, out int attemptOffset, out int headerSize)
        {
            tokenOffset = DataHeaderSize + TokenLengthSize;
            tokenLength = 0;
            attemptOffset = 0;
            headerSize = 0;
            if (count < tokenOffset)
            {
                return false;
            }

            tokenLength = BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(datagram, DataHeaderSize, TokenLengthSize));
            headerSize = ConnectHeaderSize(tokenLength);
            attemptOffset = tokenOffset + tokenLength;
            return tokenLength > 0 && count >= headerSize + DatagramCipher.TagLength;
        }
    }

    internal struct ReplayWindow
    {
        private const int Width = 64;

        private ulong highest;
        private ulong seen;
        private bool started;

        public bool IsFresh(ulong sequence)
        {
            if (!started || sequence > highest)
            {
                return true;
            }

            ulong behind = highest - sequence;
            return behind < Width && (seen & (1UL << (int)behind)) == 0;
        }

        public void Mark(ulong sequence)
        {
            if (!started)
            {
                started = true;
                highest = sequence;
                seen = 1;
                return;
            }

            if (sequence > highest)
            {
                ulong ahead = sequence - highest;
                seen = ahead >= Width ? 1 : (seen << (int)ahead) | 1;
                highest = sequence;
                return;
            }

            seen |= 1UL << (int)(highest - sequence);
        }
    }
}
