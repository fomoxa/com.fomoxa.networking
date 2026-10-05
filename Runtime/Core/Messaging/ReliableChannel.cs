using System;
using System.Collections.Generic;

namespace Fomoxa.Networking.Messaging
{
    public enum ReliableVerdict
    {
        Deliver,
        Buffer,
        Duplicate,
        OutOfWindow,
    }

    public sealed class ReliableChannel
    {
        public const int MaxWindow = 16384;
        public const int AckMaskBits = 32;

        public static readonly TimeSpan InitialRto = TimeSpan.FromMilliseconds(200);
        public static readonly TimeSpan MinRto = TimeSpan.FromMilliseconds(50);
        public static readonly TimeSpan MaxRto = TimeSpan.FromSeconds(1);

        private readonly int window;
        private readonly List<InFlight> inFlight = new List<InFlight>();
        private readonly Dictionary<ushort, Buffered> buffered = new Dictionary<ushort, Buffered>();
        private ushort nextSendSeq;
        private ushort nextExpected;
        private TimeSpan smoothedRtt;
        private TimeSpan rttVariance;
        private bool hasRttSample;

        public ReliableChannel(int window)
        {
            if (window < 1 || window > MaxWindow)
            {
                throw new ArgumentOutOfRangeException(nameof(window));
            }

            this.window = window;
            Rto = InitialRto;
        }

        public int Window => window;

        public TimeSpan Rto { get; private set; }

        public int InFlightCount => inFlight.Count;

        public ushort NextSendSeq => nextSendSeq;

        public ushort NextExpected => nextExpected;

        public int BufferedCount => buffered.Count;

        public bool AckPending { get; private set; }

        public bool CanSend(int tentative) => inFlight.Count + tentative < window;

        public void Track(uint messageId, byte[] data, TimeSpan now)
        {
            inFlight.Add(new InFlight(nextSendSeq, messageId, data, now, now, false));
            nextSendSeq++;
        }

        public void CollectDue(TimeSpan now, List<OutgoingEntry> into)
        {
            for (int index = 0; index < inFlight.Count; index++)
            {
                InFlight entry = inFlight[index];
                if (now - entry.LastSent >= Rto)
                {
                    into.Add(OutgoingEntry.Retransmit(entry.MessageId, entry.Data, index));
                }
            }
        }

        public void MarkResent(int index, TimeSpan now)
        {
            InFlight entry = inFlight[index];
            inFlight[index] = new InFlight(entry.Seq, entry.MessageId, entry.Data, entry.FirstSent, now, true);
        }

        public void OnAck(ushort next, uint received, TimeSpan now)
        {
            TimeSpan? sample = null;
            int kept = 0;
            for (int index = 0; index < inFlight.Count; index++)
            {
                InFlight entry = inFlight[index];
                int distance = Distance(entry.Seq, next);
                bool acked = distance < 0
                    || (distance >= 1 && distance <= AckMaskBits && (received & (1u << (distance - 1))) != 0);
                if (!acked)
                {
                    inFlight[kept++] = entry;
                    continue;
                }

                if (!entry.Resent)
                {
                    sample = now - entry.FirstSent;
                }
            }

            inFlight.RemoveRange(kept, inFlight.Count - kept);
            if (sample.HasValue)
            {
                UpdateRto(sample.Value);
            }
        }

        public ReliableVerdict Classify(ushort seq)
        {
            int distance = Distance(seq, nextExpected);
            if (distance == 0)
            {
                return ReliableVerdict.Deliver;
            }

            if (distance < 0)
            {
                return ReliableVerdict.Duplicate;
            }

            if (distance >= MaxWindow)
            {
                return ReliableVerdict.OutOfWindow;
            }

            return buffered.ContainsKey(seq) ? ReliableVerdict.Duplicate : ReliableVerdict.Buffer;
        }

        public void Advance()
        {
            nextExpected++;
        }

        public void Store(ushort seq, uint messageId, ReadOnlySpan<byte> data)
        {
            buffered.Add(seq, new Buffered(messageId, data.ToArray()));
        }

        public bool TryTakeNext(out uint messageId, out byte[] data)
        {
            if (!buffered.Remove(nextExpected, out Buffered next))
            {
                messageId = 0;
                data = null;
                return false;
            }

            nextExpected++;
            messageId = next.MessageId;
            data = next.Data;
            return true;
        }

        public void MarkReceived()
        {
            AckPending = true;
        }

        public void WriteAck(ReliableAck ack)
        {
            uint mask = 0;
            for (int bit = 0; bit < AckMaskBits; bit++)
            {
                if (buffered.ContainsKey((ushort)(nextExpected + 1 + bit)))
                {
                    mask |= 1u << bit;
                }
            }

            ack.Next = nextExpected;
            ack.Received = mask;
        }

        public void AckSent()
        {
            AckPending = false;
        }

        private void UpdateRto(TimeSpan sample)
        {
            if (!hasRttSample)
            {
                smoothedRtt = sample;
                rttVariance = TimeSpan.FromTicks(sample.Ticks / 2);
                hasRttSample = true;
            }
            else
            {
                long deviation = Math.Abs(smoothedRtt.Ticks - sample.Ticks);
                rttVariance = TimeSpan.FromTicks((3 * rttVariance.Ticks + deviation) / 4);
                smoothedRtt = TimeSpan.FromTicks((7 * smoothedRtt.Ticks + sample.Ticks) / 8);
            }

            long rto = smoothedRtt.Ticks + 4 * rttVariance.Ticks;
            Rto = TimeSpan.FromTicks(Math.Min(MaxRto.Ticks, Math.Max(MinRto.Ticks, rto)));
        }

        private static int Distance(ushort seq, ushort reference) => (short)(ushort)(seq - reference);

        private readonly struct InFlight
        {
            public InFlight(ushort seq, uint messageId, byte[] data, TimeSpan firstSent, TimeSpan lastSent, bool resent)
            {
                Seq = seq;
                MessageId = messageId;
                Data = data;
                FirstSent = firstSent;
                LastSent = lastSent;
                Resent = resent;
            }

            public ushort Seq { get; }

            public uint MessageId { get; }

            public byte[] Data { get; }

            public TimeSpan FirstSent { get; }

            public TimeSpan LastSent { get; }

            public bool Resent { get; }
        }

        private readonly struct Buffered
        {
            public Buffered(uint messageId, byte[] data)
            {
                MessageId = messageId;
                Data = data;
            }

            public uint MessageId { get; }

            public byte[] Data { get; }
        }
    }
}
