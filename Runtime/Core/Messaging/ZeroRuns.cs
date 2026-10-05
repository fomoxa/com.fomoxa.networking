using System;

namespace Fomoxa.Networking.Messaging
{
    public static class ZeroRuns
    {
        public const int MaxRun = byte.MaxValue;

        private const int ShortestZeroRun = 3;

        public static int Encode(ReadOnlySpan<byte> current, ReadOnlySpan<byte> baseline, ref byte[] output)
        {
            if (current.Length != baseline.Length)
            {
                throw new ArgumentException($"current has {current.Length} bytes and baseline {baseline.Length}; a delta needs equal lengths", nameof(baseline));
            }

            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            int limit = current.Length;
            while (limit > 0 && current[limit - 1] == baseline[limit - 1])
            {
                limit--;
            }

            int length = 0;
            int position = 0;
            while (position < limit)
            {
                int zeros = 0;
                while (position < limit && zeros < MaxRun && current[position] == baseline[position])
                {
                    zeros++;
                    position++;
                }

                int start = position;
                int literals = 0;
                while (position < limit && literals < MaxRun)
                {
                    if (current[position] != baseline[position])
                    {
                        literals++;
                        position++;
                        continue;
                    }

                    int gap = 0;
                    while (gap < ShortestZeroRun && position + gap < limit && current[position + gap] == baseline[position + gap])
                    {
                        gap++;
                    }

                    if (gap >= ShortestZeroRun || literals + gap > MaxRun)
                    {
                        break;
                    }

                    literals += gap;
                    position += gap;
                }

                EnsureCapacity(ref output, length + 2 + literals);
                output[length++] = (byte)zeros;
                output[length++] = (byte)literals;
                for (int index = 0; index < literals; index++)
                {
                    output[length++] = (byte)(current[start + index] ^ baseline[start + index]);
                }
            }

            return length;
        }

        public static bool TryApply(ReadOnlySpan<byte> data, Span<byte> target)
        {
            if (!Fits(data, target.Length))
            {
                return false;
            }

            int position = 0;
            int index = 0;
            while (index < data.Length)
            {
                position += data[index];
                int literals = data[index + 1];
                index += 2;
                for (int count = 0; count < literals; count++)
                {
                    target[position++] ^= data[index++];
                }
            }

            return true;
        }

        private static bool Fits(ReadOnlySpan<byte> data, int length)
        {
            int position = 0;
            int index = 0;
            while (index < data.Length)
            {
                if (data.Length - index < 2)
                {
                    return false;
                }

                int literals = data[index + 1];
                position += data[index] + literals;
                index += 2;
                if (data.Length - index < literals || position > length)
                {
                    return false;
                }

                index += literals;
            }

            return true;
        }

        private static void EnsureCapacity(ref byte[] output, int needed)
        {
            if (output.Length >= needed)
            {
                return;
            }

            var grown = new byte[Math.Max(needed, output.Length * 2)];
            output.AsSpan().CopyTo(grown);
            output = grown;
        }
    }
}
