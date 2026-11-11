using System.Collections.Generic;

namespace Fomoxa.Networking.Sessions
{
    public readonly struct TrafficCount
    {
        public TrafficCount(long count, long bytes)
        {
            Count = count;
            Bytes = bytes;
        }

        public long Count { get; }

        public long Bytes { get; }
    }

    internal enum TrafficKind
    {
        New,
        Repeated,
        Ack,
        Fragment,
        Split,
    }

    public sealed class MessageTraffic
    {
        private const int KindCount = (int)TrafficKind.Split + 1;

        private readonly long[] counts = new long[KindCount];
        private readonly long[] bytes = new long[KindCount];

        public TrafficCount New => CountOf(TrafficKind.New);

        public TrafficCount Repeated => CountOf(TrafficKind.Repeated);

        public TrafficCount Ack => CountOf(TrafficKind.Ack);

        public TrafficCount Fragment => CountOf(TrafficKind.Fragment);

        public TrafficCount Split => CountOf(TrafficKind.Split);

        internal void Add(TrafficKind kind, int length)
        {
            counts[(int)kind]++;
            bytes[(int)kind] += length;
        }

        private TrafficCount CountOf(TrafficKind kind) => new TrafficCount(counts[(int)kind], bytes[(int)kind]);
    }

    public sealed class TrafficDirection
    {
        private readonly TrafficDirection aggregate;
        private readonly Dictionary<uint, MessageTraffic> messages = new Dictionary<uint, MessageTraffic>();
        private long bundles;
        private long bundleBytes;

        internal TrafficDirection(TrafficDirection aggregate)
        {
            this.aggregate = aggregate;
        }

        public TrafficCount Bundles => new TrafficCount(bundles, bundleBytes);

        public MessageTraffic Total { get; } = new MessageTraffic();

        public IReadOnlyDictionary<uint, MessageTraffic> Messages => messages;

        internal void AddBundle(int length)
        {
            bundles++;
            bundleBytes += length;
            aggregate?.AddBundle(length);
        }

        internal void Add(TrafficKind kind, uint messageId, int length)
        {
            if (!messages.TryGetValue(messageId, out MessageTraffic row))
            {
                row = new MessageTraffic();
                messages.Add(messageId, row);
            }

            row.Add(kind, length);
            Total.Add(kind, length);
            aggregate?.Add(kind, messageId, length);
        }
    }

    public class TrafficStatistics
    {
        internal TrafficStatistics(TrafficStatistics aggregate)
        {
            Sent = new TrafficDirection(aggregate?.Sent);
            Received = new TrafficDirection(aggregate?.Received);
        }

        public TrafficDirection Sent { get; }

        public TrafficDirection Received { get; }
    }

    public sealed class ServerTrafficStatistics : TrafficStatistics
    {
        private readonly Dictionary<ulong, TrafficStatistics> peers = new Dictionary<ulong, TrafficStatistics>();

        internal ServerTrafficStatistics()
            : base(null)
        {
        }

        public TrafficStatistics Of(ulong peerId) => peers.TryGetValue(peerId, out TrafficStatistics peer) ? peer : null;

        internal TrafficStatistics AddPeer(ulong peerId)
        {
            var peer = new TrafficStatistics(this);
            peers[peerId] = peer;
            return peer;
        }

        internal void RemovePeer(ulong peerId)
        {
            peers.Remove(peerId);
        }
    }
}
