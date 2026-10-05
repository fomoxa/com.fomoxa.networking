using System;

namespace Fomoxa.Networking.Timing
{
    public sealed class ClockSettings
    {
        private TimeSpan pingInterval = TimeSpan.FromSeconds(1);
        private int inputBuffer = 2;
        private double maxSpeedAdjust = 0.1;
        private int resetThreshold = 10;

        public TimeSpan PingInterval
        {
            get => pingInterval;
            set => pingInterval = value > TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(nameof(value));
        }

        public int InputBuffer
        {
            get => inputBuffer;
            set => inputBuffer = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
        }

        public double MaxSpeedAdjust
        {
            get => maxSpeedAdjust;
            set => maxSpeedAdjust = value >= 0 && value < 1 ? value : throw new ArgumentOutOfRangeException(nameof(value));
        }

        public int ResetThreshold
        {
            get => resetThreshold;
            set => resetThreshold = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value));
        }
    }
}
