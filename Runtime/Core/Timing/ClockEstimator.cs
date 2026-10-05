using System;

namespace Fomoxa.Networking.Timing
{
    public sealed class ClockEstimator
    {
        public const double SpeedAdjustPerTick = 0.05;
        public const int OutlierFactor = 2;
        public const int OutliersBeforeAccepting = 3;
        private const double SmoothingWeight = 0.125;
        private const double TickRoundingTolerance = 1e-4;

        private readonly ClockSettings settings;
        private double smoothedRttSeconds;
        private double offsetTicks;
        private double leadTarget;
        private bool predicting;
        private int rejectedInARow;

        public ClockEstimator(ClockSettings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public int TickRate { get; private set; }

        public bool Synced { get; private set; }

        public TimeSpan Rtt => TimeSpan.FromSeconds(smoothedRttSeconds);

        public bool HasInputLead { get; private set; }

        public uint ServerTick { get; private set; }

        public uint PredictionTick { get; private set; }

        public double TickScale { get; private set; } = 1;

        public void SetTickRate(int tickRate)
        {
            if (tickRate < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(tickRate));
            }

            if (tickRate != TickRate)
            {
                Reset();
                TickRate = tickRate;
            }
        }

        public void Reset()
        {
            TickRate = 0;
            Synced = false;
            smoothedRttSeconds = 0;
            offsetTicks = 0;
            leadTarget = 0;
            predicting = false;
            rejectedInARow = 0;
            HasInputLead = false;
            ServerTick = 0;
            PredictionTick = 0;
            TickScale = 1;
        }

        public bool AddSample(uint clientTime, uint serverTick, short inputLead, TimeSpan now)
        {
            if (TickRate == 0)
            {
                return false;
            }

            double rttSeconds = unchecked(ClientTimeOf(now) - clientTime) / 1000.0;
            if (Synced && rttSeconds > OutlierFactor * smoothedRttSeconds && rejectedInARow < OutliersBeforeAccepting)
            {
                rejectedInARow++;
                return false;
            }

            rejectedInARow = 0;
            double sampleOffset = serverTick + rttSeconds / 2 * TickRate - TicksAt(now);
            if (Synced)
            {
                smoothedRttSeconds += (rttSeconds - smoothedRttSeconds) * SmoothingWeight;
                offsetTicks += (sampleOffset - offsetTicks) * SmoothingWeight;
            }
            else
            {
                smoothedRttSeconds = rttSeconds;
                offsetTicks = sampleOffset;
                Synced = true;
            }

            HasInputLead = inputLead != ClockProtocol.NoInputLead;
            if (HasInputLead && predicting)
            {
                leadTarget = PredictionTick - ServerTickAt(now) + (settings.InputBuffer - inputLead);
            }
            else
            {
                leadTarget = Math.Ceiling(smoothedRttSeconds / 2 * TickRate) + settings.InputBuffer;
            }

            return true;
        }

        public void Advance(TimeSpan now)
        {
            if (!Synced)
            {
                return;
            }

            double server = ServerTickAt(now);
            ServerTick = (uint)Math.Max(0, Math.Floor(server + TickRoundingTolerance));
            double error = PredictionTick + 1 - server - leadTarget;
            if (!predicting || Math.Abs(error) > settings.ResetThreshold)
            {
                PredictionTick = (uint)Math.Max(0, Math.Round(server + leadTarget));
                predicting = true;
                TickScale = 1;
                return;
            }

            PredictionTick++;
            TickScale = 1 + Math.Max(-settings.MaxSpeedAdjust, Math.Min(settings.MaxSpeedAdjust, SpeedAdjustPerTick * error));
        }

        public static uint ClientTimeOf(TimeSpan now) => unchecked((uint)(long)now.TotalMilliseconds);

        private double ServerTickAt(TimeSpan now) => TicksAt(now) + offsetTicks;

        private double TicksAt(TimeSpan now) => now.TotalSeconds * TickRate;
    }
}
