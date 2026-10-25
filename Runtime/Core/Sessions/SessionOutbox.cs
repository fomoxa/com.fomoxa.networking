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
        private readonly BundleFormat bundle;
        private readonly BundleWriter writer;
        private readonly List<OutgoingEntry> entries = new List<OutgoingEntry>();
        private readonly ReliableAck ack = new ReliableAck();
        private readonly FragmentStart fragmentStart = new FragmentStart();
        private readonly FragmentPart fragmentPart = new FragmentPart();
        private readonly List<QueuedMessage> leftovers = new List<QueuedMessage>();
        private readonly List<QueuedMessage> held = new List<QueuedMessage>();
        private OutgoingQueue authentication;
        private bool gated;
        private byte[] splitting;
        private int splitOffset;

        public SessionOutbox(OutgoingQueue queue, SessionProtocol protocol, ReliableChannel reliable)
            : this(queue, protocol, reliable, protocol?.Bundle)
        {
        }

        public SessionOutbox(OutgoingQueue queue, SessionProtocol protocol, ReliableChannel reliable, BundleFormat bundle)
        {
            this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
            this.protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
            this.reliable = reliable ?? throw new ArgumentNullException(nameof(reliable));
            this.bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
            writer = new BundleWriter(bundle);
            FrameBudget = bundle.FrameBudget;
        }

        public int FrameBudget { get; }

        public OutgoingQueue Queue => queue;

        public ReliableChannel Reliable => reliable;

        public bool IsGated => gated;

        public int ActiveCount => Active?.Count ?? 0;

        private OutgoingQueue Active => gated ? authentication : queue;

        public EnqueueResult Enqueue(uint messageId, ReadOnlySpan<byte> payload) => Enqueue(queue, messageId, payload);

        public EnqueueResult EnqueueAuthentication(uint messageId, ReadOnlySpan<byte> payload) =>
            Enqueue(AuthenticationQueue(), messageId, payload);

        public EnqueueResult EnqueueActiveUnbounded(uint messageId, ReadOnlySpan<byte> payload) =>
            EnqueueUnbounded(gated ? AuthenticationQueue() : queue, messageId, payload);

        public void Gate()
        {
            gated = true;
        }

        public void OpenGate(bool keepLeftovers)
        {
            gated = false;
            if (authentication == null)
            {
                return;
            }

            leftovers.Clear();
            if (keepLeftovers)
            {
                authentication.MoveAllTo(leftovers);
            }
            else if (splitting != null && authentication.Count > 0 && ReferenceEquals(HeadOf(authentication).Payload, splitting))
            {
                leftovers.Add(HeadOf(authentication));
            }

            authentication = null;
            if (leftovers.Count == 0)
            {
                return;
            }

            held.Clear();
            queue.MoveAllTo(held);
            queue.AppendAll(leftovers);
            queue.AppendAll(held);
            leftovers.Clear();
            held.Clear();
        }

        public int Clear() => queue.Clear() + (authentication?.Clear() ?? 0);

        private OutgoingQueue AuthenticationQueue() =>
            authentication ??= new OutgoingQueue(queue.Capacity, queue.ByteCapacity);

        private EnqueueResult Enqueue(OutgoingQueue target, uint messageId, ReadOnlySpan<byte> payload)
        {
            bool isReliable = protocol.Channels.IsReliable(messageId);
            if (isReliable && CannotReassemble(SeqHeader.Length + payload.Length))
            {
                return EnqueueResult.TooLargeForReassembly;
            }

            Span<byte> header = stackalloc byte[SeqHeader.Length];
            return target.Enqueue(messageId, isReliable ? header : Span<byte>.Empty, payload);
        }

        public EnqueueResult EnqueueUnbounded(uint messageId, ReadOnlySpan<byte> payload) => EnqueueUnbounded(queue, messageId, payload);

        private EnqueueResult EnqueueUnbounded(OutgoingQueue target, uint messageId, ReadOnlySpan<byte> payload)
        {
            Span<byte> header = stackalloc byte[SeqHeader.Length];
            return target.EnqueueUnbounded(messageId, protocol.Channels.IsReliable(messageId) ? header : Span<byte>.Empty, payload);
        }

        public EnqueueResult EnqueueToObject(uint messageId, uint objectId, byte behaviourIndex, ReadOnlySpan<byte> body)
        {
            int seqLength = protocol.Channels.IsReliable(messageId) ? SeqHeader.Length : 0;
            if (seqLength > 0 && CannotReassemble(seqLength + ObjectHeader.UnreliableLength + body.Length))
            {
                return EnqueueResult.TooLargeForReassembly;
            }

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
                if (outcome == FlushOutcome.Rebuild || (outcome == FlushOutcome.Drained && DropUnsplittableHead(peerId, dropped)))
                {
                    continue;
                }

                return discarded;
            }
        }

        private bool NeedsSplit(int dataLength) =>
            BundleFormat.BundleOverhead + BundleFormat.EntryOverhead + dataLength > bundle.PayloadBudget;

        private bool CannotReassemble(int dataLength) =>
            NeedsSplit(dataLength) && dataLength - SeqHeader.Length > Fragments.MaxLength;

        private bool IsUnsplittable(QueuedMessage message) =>
            protocol.Channels.IsReliable(message.MessageId) && CannotReassemble(message.Payload.Length);

        private void Collect(TimeSpan now)
        {
            entries.Clear();
            if (reliable.AckPending)
            {
                reliable.WriteAck(ack);
                entries.Add(OutgoingEntry.Ack(protocol.AckCodec.MessageId, protocol.AckCodec.Encode(ack)));
            }

            reliable.CollectDue(now, entries);
            OutgoingQueue active = Active;
            if (active == null)
            {
                return;
            }

            int tentative = 0;
            foreach (QueuedMessage message in active)
            {
                if (!protocol.Channels.IsReliable(message.MessageId))
                {
                    entries.Add(OutgoingEntry.Queued(message.MessageId, message.Payload));
                    continue;
                }

                if (NeedsSplit(message.Payload.Length))
                {
                    if (IsUnsplittable(message) || !CollectFragments(message, ref tentative))
                    {
                        break;
                    }

                    continue;
                }

                if (!reliable.CanSend(tentative))
                {
                    break;
                }

                SeqHeader.Write(message.Payload, (ushort)(reliable.NextSendSeq + tentative));
                tentative++;
                entries.Add(OutgoingEntry.Queued(message.MessageId, message.Payload));
            }
        }

        private bool CollectFragments(QueuedMessage message, ref int tentative)
        {
            ReadOnlyMemory<byte> content = message.Payload.AsMemory(SeqHeader.Length);
            int offset = ReferenceEquals(message.Payload, splitting) ? splitOffset : 0;
            int room = bundle.PayloadBudget - BundleFormat.BundleOverhead - BundleFormat.EntryOverhead - SeqHeader.Length;
            while (offset < content.Length)
            {
                if (!reliable.CanSend(tentative))
                {
                    return false;
                }

                IMessageCodec<FragmentStart> startCodec = protocol.FragmentStartCodec;
                IMessageCodec<FragmentPart> partCodec = protocol.FragmentPartCodec;
                ReadOnlyMemory<byte> encoded;
                uint fragmentId;
                int end;
                if (offset == 0)
                {
                    end = Math.Min(content.Length, room - Fragments.StartOverhead);
                    fragmentStart.MessageId = message.MessageId;
                    fragmentStart.Length = (uint)content.Length;
                    fragmentStart.Data = content.Slice(0, end);
                    encoded = startCodec.Encode(fragmentStart);
                    fragmentStart.Data = default;
                    fragmentId = startCodec.MessageId;
                }
                else
                {
                    end = Math.Min(content.Length, offset + room - Fragments.PartOverhead);
                    fragmentPart.Data = content.Slice(offset, end - offset);
                    encoded = partCodec.Encode(fragmentPart);
                    fragmentPart.Data = default;
                    fragmentId = partCodec.MessageId;
                }

                var data = new byte[SeqHeader.Length + encoded.Length];
                SeqHeader.Write(data, (ushort)(reliable.NextSendSeq + tentative));
                encoded.Span.CopyTo(data.AsSpan(SeqHeader.Length));
                tentative++;
                entries.Add(OutgoingEntry.Fragment(fragmentId, data, end));
                offset = end;
            }

            return true;
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
                            case OutgoingKind.Fragment:
                                return FlushOutcome.Stop;
                            default:
                                Active.TryDropNext(out uint messageId, out int payloadLength);
                                dropped?.Invoke(new SendDroppedArgs(peerId, messageId, payloadLength, SendStatus.TooLarge));
                                return FlushOutcome.Rebuild;
                        }
                    case SendStatus.Closed:
                        discarded = Active?.Clear() ?? 0;
                        return FlushOutcome.Stop;
                    default:
                        return FlushOutcome.Stop;
                }
            }

            return FlushOutcome.Drained;
        }

        private bool DropUnsplittableHead(ulong peerId, Action<SendDroppedArgs> dropped)
        {
            if (ActiveCount == 0 || !IsUnsplittable(HeadOfQueue()))
            {
                return false;
            }

            Active.TryDropNext(out uint messageId, out int payloadLength);
            dropped?.Invoke(new SendDroppedArgs(peerId, messageId, payloadLength, SendStatus.TooLarge));
            return true;
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
                    case OutgoingKind.Fragment:
                        CommitFragment(entry, now);
                        break;
                    default:
                        QueuedMessage sent = HeadOfQueue();
                        Active.Dequeue(1);
                        if (protocol.Channels.IsReliable(sent.MessageId))
                        {
                            reliable.Track(sent.MessageId, sent.Payload, now);
                        }

                        break;
                }
            }
        }

        private void CommitFragment(OutgoingEntry entry, TimeSpan now)
        {
            reliable.Track(entry.MessageId, entry.Data, now);
            QueuedMessage head = HeadOfQueue();
            if (entry.SplitEnd < head.Payload.Length - SeqHeader.Length)
            {
                splitting = head.Payload;
                splitOffset = entry.SplitEnd;
                return;
            }

            Active.Dequeue(1);
            splitting = null;
            splitOffset = 0;
        }

        private QueuedMessage HeadOfQueue() => HeadOf(Active);

        private static QueuedMessage HeadOf(OutgoingQueue source)
        {
            foreach (QueuedMessage message in source)
            {
                return message;
            }

            throw new InvalidOperationException("the outgoing queue is empty");
        }

        private enum FlushOutcome
        {
            Stop,
            Rebuild,
            Drained,
        }
    }
}
