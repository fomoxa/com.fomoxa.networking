using System;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking
{
    public sealed class NetworkSettings
    {
        private int tickRate = 30;
        private int maxTicksPerFrame = 3;
        private int inputRedundancy = 3;
        private int predictionHistory = 64;
        private int reconcileInterval = 1;
        private int observerInterval = 15;

        public int TickRate
        {
            get => tickRate;
            set => tickRate = value >= 1 && value <= ushort.MaxValue ? value : throw new ArgumentOutOfRangeException(nameof(value), "the tick rate is between 1 and 65535");
        }

        public int MaxTicksPerFrame
        {
            get => maxTicksPerFrame;
            set => maxTicksPerFrame = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), "a frame runs at least one tick");
        }

        public TimingMode TimingMode { get; set; } = TimingMode.Tick;

        public int MessageCapacity { get; set; } = 1024;

        public int ByteCapacity { get; set; } = 1024 * 1024;

        public int ReliableWindow { get; set; } = 1024;

        public bool LogSendDropped { get; set; } = true;

        public bool LogReceiveDropped { get; set; } = true;

        public int MaxReconnectRetries { get; set; } = -1;

        public TimeSpan ReconnectInterval { get; set; } = TimeSpan.FromSeconds(0.5);

        public TimeSpan HandshakeTimeout { get; set; } = TimeSpan.FromSeconds(5);

        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);

        public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(15);

        public int ObserverInterval
        {
            get => observerInterval;
            set => observerInterval = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), "the observer interval is at least one tick");
        }

        public TimeSpan ClockPingInterval { get; set; } = TimeSpan.FromSeconds(1);

        public int InputBuffer { get; set; } = 2;

        public double MaxTickSpeedAdjust { get; set; } = 0.1;

        public int ClockResetTicks { get; set; } = 10;

        public int InputRedundancy
        {
            get => inputRedundancy;
            set => inputRedundancy = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), "an input frame carries at least one input");
        }

        public int MaxInputLead { get; set; } = 30;

        public int PredictionHistory
        {
            get => predictionHistory;
            set => predictionHistory = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), "the prediction history keeps at least one tick");
        }

        public int ReconcileInterval
        {
            get => reconcileInterval;
            set => reconcileInterval = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), "the reconcile interval is at least one tick");
        }

        public PhysicsBackend PhysicsBackend { get; set; } = PhysicsBackend.Rigidbody;
    }
}
