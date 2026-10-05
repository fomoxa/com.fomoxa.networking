using System;
using System.Collections.Generic;
using Fomoxa.Net;

namespace Fomoxa.Networking.Messaging
{
    public delegate SendStatus MessageSender(uint messageId, ReadOnlySpan<byte> payload);

    public enum EnqueueResult
    {
        Queued,
        Full,
        BytesFull,
        TooLargeForQueue,
        TooLarge,
    }

    public readonly struct QueuedMessage
    {
        public QueuedMessage(uint messageId, byte[] payload)
        {
            MessageId = messageId;
            Payload = payload;
        }

        public uint MessageId { get; }

        public byte[] Payload { get; }
    }

    public sealed class OutgoingQueue
    {
        private readonly Queue<QueuedMessage> pending = new Queue<QueuedMessage>();
        private readonly int capacity;
        private readonly int byteCapacity;
        private int pendingBytes;

        public OutgoingQueue(int capacity, int byteCapacity)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (byteCapacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(byteCapacity));
            }

            this.capacity = capacity;
            this.byteCapacity = byteCapacity;
        }

        public int Capacity => capacity;

        public int ByteCapacity => byteCapacity;

        public int Count => pending.Count;

        public int PendingBytes => pendingBytes;

        public EnqueueResult Enqueue(uint messageId, ReadOnlySpan<byte> payload) =>
            Enqueue(messageId, ReadOnlySpan<byte>.Empty, payload);

        public EnqueueResult Enqueue(uint messageId, ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload) =>
            Enqueue(messageId, header, payload, true);

        public EnqueueResult EnqueueUnbounded(uint messageId, ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload) =>
            Enqueue(messageId, header, payload, false);

        public void MoveAllTo(List<QueuedMessage> destination)
        {
            destination.AddRange(pending);
            Clear();
        }

        public void AppendAll(List<QueuedMessage> messages)
        {
            foreach (QueuedMessage message in messages)
            {
                pending.Enqueue(message);
                pendingBytes += message.Payload.Length;
            }
        }

        private EnqueueResult Enqueue(uint messageId, ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload, bool bounded)
        {
            if (payload.Length > BundleFormat.MaxEntryPayload - header.Length)
            {
                return EnqueueResult.TooLarge;
            }

            int length = header.Length + payload.Length;
            if (bounded)
            {
                if (length > byteCapacity)
                {
                    return EnqueueResult.TooLargeForQueue;
                }

                if (pending.Count >= capacity)
                {
                    return EnqueueResult.Full;
                }

                if (length > byteCapacity - pendingBytes)
                {
                    return EnqueueResult.BytesFull;
                }
            }

            var data = new byte[length];
            header.CopyTo(data);
            payload.CopyTo(data.AsSpan(header.Length));
            pending.Enqueue(new QueuedMessage(messageId, data));
            pendingBytes += length;
            return EnqueueResult.Queued;
        }

        public Queue<QueuedMessage>.Enumerator GetEnumerator() => pending.GetEnumerator();

        public void Dequeue(int count)
        {
            if (count < 0 || count > pending.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            for (int index = 0; index < count; index++)
            {
                pendingBytes -= pending.Dequeue().Payload.Length;
            }
        }

        public bool TryDropNext(out uint messageId, out int payloadLength)
        {
            if (pending.Count == 0)
            {
                messageId = 0;
                payloadLength = 0;
                return false;
            }

            QueuedMessage dropped = pending.Dequeue();
            pendingBytes -= dropped.Payload.Length;
            messageId = dropped.MessageId;
            payloadLength = dropped.Payload.Length;
            return true;
        }

        public int Clear()
        {
            int cleared = pending.Count;
            pending.Clear();
            pendingBytes = 0;
            return cleared;
        }
    }
}
