using System;

namespace Fomoxa.Networking.Transports
{
    public sealed class NetworkConditions
    {
        private TimeSpan latency;
        private TimeSpan jitter;
        private double packetLoss;
        private double duplicate;
        private double reorder;
        private int maxHeldPackets = 1024;

        public TimeSpan Latency
        {
            get => latency;
            set => latency = value >= TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(nameof(value), "the latency is not negative");
        }

        public TimeSpan Jitter
        {
            get => jitter;
            set => jitter = value >= TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(nameof(value), "the jitter is not negative");
        }

        public double PacketLoss
        {
            get => packetLoss;
            set => packetLoss = Percent(value);
        }

        public double Duplicate
        {
            get => duplicate;
            set => duplicate = Percent(value);
        }

        public double Reorder
        {
            get => reorder;
            set => reorder = Percent(value);
        }

        public int MaxHeldPackets
        {
            get => maxHeldPackets;
            set => maxHeldPackets = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), "at least one packet is held");
        }

        private static double Percent(double value) =>
            value >= 0 && value <= 100 ? value : throw new ArgumentOutOfRangeException(nameof(value), "a percentage is between 0 and 100");
    }
}
