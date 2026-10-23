using System;

namespace Fomoxa.Networking.Messaging
{
    public sealed class FragmentAssembler
    {
        private byte[] buffer;
        private int filled;
        private uint messageId;

        public bool IsAssembling => buffer != null;

        public bool Start(uint messageId, uint length, ReadOnlySpan<byte> data)
        {
            if (buffer != null || length > Fragments.MaxLength || data.Length >= length)
            {
                return false;
            }

            buffer = new byte[length];
            data.CopyTo(buffer);
            filled = data.Length;
            this.messageId = messageId;
            return true;
        }

        public bool Append(ReadOnlySpan<byte> data)
        {
            if (buffer == null || data.Length > buffer.Length - filled)
            {
                return false;
            }

            data.CopyTo(buffer.AsSpan(filled));
            filled += data.Length;
            return true;
        }

        public bool TryTake(out uint takenMessageId, out byte[] message)
        {
            if (buffer == null || filled < buffer.Length)
            {
                takenMessageId = 0;
                message = null;
                return false;
            }

            takenMessageId = messageId;
            message = buffer;
            buffer = null;
            filled = 0;
            return true;
        }
    }
}
