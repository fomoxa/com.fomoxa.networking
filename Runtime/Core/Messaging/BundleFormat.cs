using System;
using Fomoxa.Net;

namespace Fomoxa.Networking.Messaging
{
    public sealed class BundleFormat
    {
        public const int BundleOverhead = 4;
        public const int EntryOverhead = 8;
        public const int MaxEntryPayload = FomoxaWire.MaxMessagePayload - BundleOverhead - EntryOverhead;

        public BundleFormat(IMessageCodec<MessageBundle> codec, int frameBudget)
        {
            Codec = codec ?? throw new ArgumentNullException(nameof(codec));
            if (frameBudget <= FomoxaWire.DataFrameHeaderSize + BundleOverhead + EntryOverhead)
            {
                throw new ArgumentOutOfRangeException(nameof(frameBudget));
            }

            FrameBudget = Math.Min(frameBudget, FomoxaWire.MaxDataFrameSize);
        }

        public IMessageCodec<MessageBundle> Codec { get; }

        public int FrameBudget { get; }

        public int PayloadBudget => FrameBudget - FomoxaWire.DataFrameHeaderSize;
    }
}
