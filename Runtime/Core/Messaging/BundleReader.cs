using System;

namespace Fomoxa.Networking.Messaging
{
    public sealed class BundleReader
    {
        private readonly BundleFormat format;
        private MessageBundle bundle = new MessageBundle();

        public BundleReader(BundleFormat format)
        {
            this.format = format ?? throw new ArgumentNullException(nameof(format));
        }

        public int Count => bundle.Entries.Count;

        public uint MessageIdAt(int index) => bundle.Entries[index].MessageId;

        public ReadOnlyMemory<byte> DataAt(int index) => bundle.Entries[index].Data;

        public bool TryRead(uint messageId, ReadOnlyMemory<byte> payload)
        {
            if (messageId != format.Codec.MessageId)
            {
                bundle.Entries.Clear();
                return false;
            }

            try
            {
                format.Codec.Decode(payload, ref bundle);
                return true;
            }
            catch (MessageDecodeException)
            {
                Release();
                bundle.Entries.Clear();
                return false;
            }
        }

        public void Release()
        {
            foreach (BundleEntry entry in bundle.Entries)
            {
                entry.Data = default;
            }
        }
    }
}
