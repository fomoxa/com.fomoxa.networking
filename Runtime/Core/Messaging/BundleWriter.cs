using System;
using System.Collections.Generic;

namespace Fomoxa.Networking.Messaging
{
    public sealed class BundleWriter
    {
        private readonly BundleFormat format;
        private readonly MessageBundle bundle = new MessageBundle();
        private readonly List<BundleEntry> entries = new List<BundleEntry>();

        public BundleWriter(BundleFormat format)
        {
            this.format = format ?? throw new ArgumentNullException(nameof(format));
        }

        public uint MessageId => format.Codec.MessageId;

        public int Fit(IReadOnlyList<OutgoingEntry> source, int start, int maxEntries)
        {
            int size = BundleFormat.BundleOverhead;
            int count = 0;
            for (int index = start; index < source.Count && count < maxEntries; index++)
            {
                int next = size + BundleFormat.EntryOverhead + source[index].Data.Length;
                if (count > 0 && next > format.PayloadBudget)
                {
                    break;
                }

                size = next;
                count++;
            }

            return count;
        }

        public ReadOnlyMemory<byte> Write(IReadOnlyList<OutgoingEntry> source, int start, int count)
        {
            bundle.Entries.Clear();
            for (int index = start; index < start + count; index++)
            {
                if (entries.Count == bundle.Entries.Count)
                {
                    entries.Add(new BundleEntry());
                }

                BundleEntry entry = entries[bundle.Entries.Count];
                entry.MessageId = source[index].MessageId;
                entry.Data = source[index].Data;
                bundle.Entries.Add(entry);
            }

            ReadOnlyMemory<byte> encoded = format.Codec.Encode(bundle);
            foreach (BundleEntry entry in bundle.Entries)
            {
                entry.Data = default;
            }

            return encoded;
        }
    }
}
