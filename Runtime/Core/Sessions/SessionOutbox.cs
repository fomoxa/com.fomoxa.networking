using System;
using System.Collections.Generic;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Networking.Sessions
{
    public sealed class SessionOutbox
    {
        private readonly OutgoingQueue queue;
        private readonly SessionProtocol protocol;
        private readonly ReliableChannel reliable;
        private readonly BundleWriter writer;
        private readonly List<OutgoingEntry> entries = new List<OutgoingEntry>();
        private readonly ReliableAck ack = new ReliableAck();

        public SessionOutbox(OutgoingQueue queue, SessionProtocol protocol, ReliableChannel reliable)
            : this(queue, protocol, reliable, protocol?.Bundle)
        {
        }

        public SessionOutbox(OutgoingQueue queue, SessionProtocol protocol, ReliableChannel reliable, BundleFormat bundle)
        {
            this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
            this.protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
            this.reliable = reliable ?? throw new ArgumentNullException(nameof(reliable));
            writer = new BundleWriter(bundle ?? throw new ArgumentNullException(nameof(bundle)));
            FrameBudget = bundle.FrameBudget;
        }

        public int FrameBudget { get; }

        public OutgoingQueue Queue => queue;

        public ReliableChannel Reliable => reliable;

        public EnqueueResult Enqueue(uint messageId, ReadOnlySpan<byte> payload)
        {
            Span<byte> header = stackalloc byte[SeqHeader.Length];
            return queue.Enqueue(messageId, protocol.Channels.IsReliable(messageId) ? header : Span<byte>.Empty, payload);
        }

        public EnqueueResult EnqueueUnbounded(uint messageId, ReadOnlySpan<byte> payload)
        {
            Span<byte> header = stackalloc byte[SeqHeader.Length];
            return queue.EnqueueUnbounded(messageId, protocol.Channels.IsReliable(messageId) ? header : Span<byte>.Empty, payload);
        }

        public EnqueueResult EnqueueToObject(uint messageId, uint objectId, byte behaviourIndex, ReadOnlySpan<byte> body)
        {
            int seqLength = protocol.Channels.IsReliable(messageId) ? SeqHeader.Length : 0;
            Span<byte> header = stackalloc byte[SeqHeader.Length + ObjectHeader.UnreliableLength];
            ObjectHeader.WriteUnreliable(header.Slice(seqLength), objectId, behaviourIndex);
            return queue.Enqueue(messageId, header.Slice(0, seqLength + ObjectHeader.UnreliableLength), body);
        }

        public int Flush(MessageSender sender, TimeSpan now, ulong peerId, Action<SendDroppedArgs> dropped)
        {
            while (true)
            {
                Collect(now);
                FlushOutcome outcome = SendCollected(sender, now, peerId, dropped, out int discarded);
                if (outcome != FlushOutcome.Rebuild)
                {
                    return discarded;
                }
            }
        }

        private void Collect(TimeSpan now)
        {
            entries.Clear();
            if (reliable.AckPending)
            {
                reliable.WriteAck(ack);
                entries.Add(OutgoingEntry.Ack(protocol.AckCodec.MessageId, protocol.AckCodec.Encode(ack)));
            }

            reliable.CollectDue(now, entries);
            int tentative = 0;
            foreach (QueuedMessage message in queue)
            {
                if (protocol.Channels.IsReliable(message.MessageId))
                {
                    if (!reliable.CanSend(tentative))
                    {
                        break;
                    }

                    SeqHeader.Write(message.Payload, (ushort)(reliable.NextSendSeq + tentative));
                    tentative++;
                }

                entries.Add(OutgoingEntry.Queued(message.MessageId, message.Payload));
            }
        }

        private FlushOutcome SendCollected(MessageSender sender, TimeSpan now, ulong peerId, Action<SendDroppedArgs> dropped, out int discarded)
        {
            discarded = 0;
            int start = 0;
            int maxEntries = int.MaxValue;
            while (start < entries.Count)
            {
                int count = writer.Fit(entries, start, maxEntries);
                SendStatus status = sender(writer.MessageId, writer.Write(entries, start, count).Span);
                switch (status)
                {
                    case SendStatus.Sent:
                        Commit(start, count, now);
                        start += count;
                        maxEntries = int.MaxValue;
                        continue;
                    case SendStatus.TooLarge when count > 1:
                        maxEntries = count / 2;
                        continue;
                    case SendStatus.TooLarge:
                        switch (entries[start].Kind)
                        {
                            case OutgoingKind.Ack:
                                reliable.AckSent();
                                start++;
                                maxEntries = int.MaxValue;
                                continue;
                            case OutgoingKind.Retransmit:
                                return FlushOutcome.Stop;
                            default:
                                queue.TryDropNext(out uint messageId, out int payloadLength);
                                dropped?.Invoke(new SendDroppedArgs(peerId, messageId, payloadLength, SendStatus.TooLarge));
                                return FlushOutcome.Rebuild;
                        }
                    case SendStatus.Closed:
                        discarded = queue.Clear();
                        return FlushOutcome.Stop;
                    default:
                        return FlushOutcome.Stop;
                }
            }

            return FlushOutcome.Stop;
        }

        private void Commit(int start, int count, TimeSpan now)
        {
            for (int index = start; index < start + count; index++)
            {
                OutgoingEntry entry = entries[index];
                switch (entry.Kind)
                {
                    case OutgoingKind.Ack:
                        reliable.AckSent();
                        break;
                    case OutgoingKind.Retransmit:
                        reliable.MarkResent(entry.InFlightIndex, now);
                        break;
                    default:
                        QueuedMessage sent = HeadOfQueue();
                        queue.Dequeue(1);
                        if (protocol.Channels.IsReliable(sent.MessageId))
                        {
                            reliable.Track(sent.MessageId, sent.Payload, now);
                        }

                        break;
                }
            }
        }

        private QueuedMessage HeadOfQueue()
        {
            foreach (QueuedMessage message in queue)
            {
                return message;
            }

            throw new InvalidOperationException("the outgoing queue is empty");
        }

        private enum FlushOutcome
        {
            Stop,
            Rebuild,
        }
    }
}
