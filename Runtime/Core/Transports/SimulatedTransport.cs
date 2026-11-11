using System;
using System.Collections.Generic;
using Fomoxa.Net.Transports;

namespace Fomoxa.Networking.Transports
{
    internal sealed class SimulatedTransport : ITransport, IConnectionData, IGatewayArrival
    {
        private const int InitialScratch = 2048;

        private readonly ITransport inner;
        private readonly NetworkConditions conditions;
        private readonly Func<TimeSpan> clock;
        private readonly Random random;
        private readonly HeldPackets outgoing = new HeldPackets();
        private readonly HeldPackets incoming = new HeldPackets();
        private byte[] scratch = new byte[InitialScratch];
        private SendOutcome? sendEnd;
        private ReceiveOutcome? receiveEnd;

        public SimulatedTransport(ITransport inner, NetworkConditions conditions, Func<TimeSpan> clock, Random random)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.conditions = conditions;
            this.clock = clock;
            this.random = random;
        }

        public TransportKind Kind => inner.Kind;

        public ReadOnlyMemory<byte> ConnectionData => (inner as IConnectionData)?.ConnectionData ?? ReadOnlyMemory<byte>.Empty;

        public bool ArrivedOverWebSocket => (inner as IGatewayArrival)?.ArrivedOverWebSocket ?? false;

        private bool IsStream => inner.Kind == TransportKind.Stream;

        public SendOutcome Send(ReadOnlySpan<byte> bytes)
        {
            TimeSpan now = clock();
            Release(now);
            if (sendEnd.HasValue)
            {
                return sendEnd.Value;
            }

            Fate fate = FateOf(now);
            if (fate.Lost)
            {
                return SendOutcome.Ok;
            }

            if (outgoing.Count == 0 && fate.IsImmediate)
            {
                return inner.Send(bytes);
            }

            if (!Hold(outgoing, bytes, fate))
            {
                return SendOutcome.WouldBlock;
            }

            Release(now);
            return sendEnd ?? SendOutcome.Ok;
        }

        public ReceiveOutcome Receive(Span<byte> buffer)
        {
            TimeSpan now = clock();
            Release(now);
            int pulls = conditions.MaxHeldPackets + 1;
            while (pulls-- > 0 && !receiveEnd.HasValue && !(IsStream && incoming.Count >= conditions.MaxHeldPackets))
            {
                if (incoming.Count == 0)
                {
                    ReceiveOutcome direct = inner.Receive(buffer);
                    if (direct.Signal != TransportSignal.Ok)
                    {
                        if (direct.Signal == TransportSignal.NeedCapacity)
                        {
                            return direct;
                        }

                        EndReceiveOn(direct);
                        break;
                    }

                    Fate fate = FateOf(now);
                    if (fate.Lost)
                    {
                        continue;
                    }

                    if (fate.IsImmediate)
                    {
                        return direct;
                    }

                    Hold(incoming, buffer.Slice(0, direct.Count), fate);
                    continue;
                }

                ReceiveOutcome pulled = inner.Receive(scratch);
                if (pulled.Signal == TransportSignal.NeedCapacity)
                {
                    scratch = new byte[pulled.Count];
                    continue;
                }

                if (pulled.Signal != TransportSignal.Ok)
                {
                    EndReceiveOn(pulled);
                    break;
                }

                Fate pulledFate = FateOf(now);
                if (!pulledFate.Lost)
                {
                    Hold(incoming, scratch.AsSpan(0, pulled.Count), pulledFate);
                }
            }

            return Deliver(buffer, now);
        }

        public void CloseGracefully()
        {
            Release(TimeSpan.MaxValue);
            inner.CloseGracefully();
        }

        public void Dispose()
        {
            outgoing.Clear();
            incoming.Clear();
            inner.Dispose();
        }

        private void EndReceiveOn(ReceiveOutcome outcome)
        {
            if (outcome.Signal == TransportSignal.Closed || outcome.Signal == TransportSignal.Error)
            {
                receiveEnd = outcome;
            }
        }

        private Fate FateOf(TimeSpan now)
        {
            if (IsStream)
            {
                return new Fate(now, DelayOf(), false, false, false, TimeSpan.Zero);
            }

            bool lost = Chance(conditions.PacketLoss);
            TimeSpan delay = DelayOf();
            bool reordered = Chance(conditions.Reorder);
            bool duplicated = Chance(conditions.Duplicate);
            TimeSpan duplicateDelay = duplicated ? DelayOf() : TimeSpan.Zero;
            return new Fate(now, delay, lost, reordered, duplicated, duplicateDelay);
        }

        private TimeSpan DelayOf()
        {
            double milliseconds = conditions.Latency.TotalMilliseconds + (random.NextDouble() * 2 - 1) * conditions.Jitter.TotalMilliseconds;
            return milliseconds > 0 ? TimeSpan.FromMilliseconds(milliseconds) : TimeSpan.Zero;
        }

        private bool Chance(double percent) => percent > 0 && random.NextDouble() * 100 < percent;

        private bool Hold(HeldPackets held, ReadOnlySpan<byte> bytes, Fate fate)
        {
            if (IsStream)
            {
                if (held.Count >= conditions.MaxHeldPackets)
                {
                    return false;
                }

                held.Add(bytes, held.Count == 0 ? fate.ReleaseAt : Later(fate.ReleaseAt, held.LastReleaseAt), false);
                return true;
            }

            AddPacket(held, bytes, fate.ReorderDeadline, fate.Reordered);
            if (fate.Duplicated)
            {
                AddPacket(held, bytes, fate.DuplicateReleaseAt, false);
            }

            return true;
        }

        private void AddPacket(HeldPackets held, ReadOnlySpan<byte> bytes, TimeSpan releaseAt, bool waitsForNext)
        {
            if (held.Count >= conditions.MaxHeldPackets)
            {
                held.RemoveOldest();
            }

            held.Add(bytes, releaseAt, waitsForNext);
        }

        private static TimeSpan Later(TimeSpan first, TimeSpan second) => first > second ? first : second;

        private void Release(TimeSpan now)
        {
            while (!sendEnd.HasValue && outgoing.TryFirstDue(now, out HeldPacket packet))
            {
                SendOutcome outcome = inner.Send(packet.Remaining);
                switch (outcome.Signal)
                {
                    case TransportSignal.Ok:
                        outgoing.Remove(packet);
                        outgoing.ReleaseWaitingBefore(packet, now);
                        break;
                    case TransportSignal.Partial:
                        packet.Offset += outcome.Count;
                        return;
                    case TransportSignal.TooLarge:
                        outgoing.Remove(packet);
                        break;
                    case TransportSignal.Closed:
                    case TransportSignal.Error:
                        sendEnd = outcome;
                        outgoing.Clear();
                        return;
                    default:
                        return;
                }
            }
        }

        private ReceiveOutcome Deliver(Span<byte> buffer, TimeSpan now)
        {
            if (!incoming.TryFirstDue(now, out HeldPacket packet))
            {
                return incoming.Count == 0 && receiveEnd.HasValue ? receiveEnd.Value : ReceiveOutcome.WouldBlock;
            }

            ReadOnlySpan<byte> remaining = packet.Remaining;
            if (!IsStream && buffer.Length < remaining.Length)
            {
                return ReceiveOutcome.NeedCapacity(remaining.Length);
            }

            int count = Math.Min(buffer.Length, remaining.Length);
            remaining.Slice(0, count).CopyTo(buffer);
            packet.Offset += count;
            if (packet.Remaining.Length == 0)
            {
                incoming.Remove(packet);
                incoming.ReleaseWaitingBefore(packet, now);
            }

            return ReceiveOutcome.Ok(count);
        }

        private readonly struct Fate
        {
            public Fate(TimeSpan now, TimeSpan delay, bool lost, bool reordered, bool duplicated, TimeSpan duplicateDelay)
            {
                Lost = lost;
                ReleaseAt = now + delay;
                Reordered = reordered;
                Duplicated = duplicated;
                DuplicateReleaseAt = now + duplicateDelay;
                ReorderDeadline = reordered ? now + delay + delay : now + delay;
                IsImmediate = !reordered && !duplicated && delay == TimeSpan.Zero;
            }

            public bool Lost { get; }

            public TimeSpan ReleaseAt { get; }

            public bool Reordered { get; }

            public bool Duplicated { get; }

            public TimeSpan DuplicateReleaseAt { get; }

            public TimeSpan ReorderDeadline { get; }

            public bool IsImmediate { get; }
        }
    }

    internal sealed class HeldPacket
    {
        public HeldPacket(byte[] data, TimeSpan releaseAt, long sequence, bool waitsForNext)
        {
            Data = data;
            ReleaseAt = releaseAt;
            Sequence = sequence;
            WaitsForNext = waitsForNext;
        }

        public byte[] Data { get; }

        public int Offset { get; set; }

        public TimeSpan ReleaseAt { get; set; }

        public long Sequence { get; }

        public bool WaitsForNext { get; set; }

        public ReadOnlySpan<byte> Remaining => Data.AsSpan(Offset);
    }

    internal sealed class HeldPackets
    {
        private readonly List<HeldPacket> packets = new List<HeldPacket>();
        private long nextSequence;

        public int Count => packets.Count;

        public TimeSpan LastReleaseAt { get; private set; }

        public void Add(ReadOnlySpan<byte> bytes, TimeSpan releaseAt, bool waitsForNext)
        {
            Insert(new HeldPacket(bytes.ToArray(), releaseAt, nextSequence++, waitsForNext));
            LastReleaseAt = releaseAt;
        }

        public bool TryFirstDue(TimeSpan now, out HeldPacket packet)
        {
            packet = packets.Count > 0 && packets[0].ReleaseAt <= now ? packets[0] : null;
            return packet != null;
        }

        public void Remove(HeldPacket packet) => packets.Remove(packet);

        public void RemoveOldest()
        {
            int oldest = 0;
            for (int index = 1; index < packets.Count; index++)
            {
                if (packets[index].Sequence < packets[oldest].Sequence)
                {
                    oldest = index;
                }
            }

            packets.RemoveAt(oldest);
        }

        public void ReleaseWaitingBefore(HeldPacket released, TimeSpan now)
        {
            if (released.WaitsForNext)
            {
                return;
            }

            for (int index = packets.Count - 1; index >= 0; index--)
            {
                HeldPacket waiting = packets[index];
                if (waiting.WaitsForNext && waiting.Sequence < released.Sequence)
                {
                    packets.RemoveAt(index);
                    waiting.WaitsForNext = false;
                    waiting.ReleaseAt = now < waiting.ReleaseAt ? now : waiting.ReleaseAt;
                    Insert(waiting);
                }
            }
        }

        public void Clear() => packets.Clear();

        private void Insert(HeldPacket packet)
        {
            int index = packets.Count;
            while (index > 0 && Precedes(packet, packets[index - 1]))
            {
                index--;
            }

            packets.Insert(index, packet);
        }

        private static bool Precedes(HeldPacket first, HeldPacket second) =>
            first.ReleaseAt < second.ReleaseAt || (first.ReleaseAt == second.ReleaseAt && first.Sequence < second.Sequence);
    }
}
