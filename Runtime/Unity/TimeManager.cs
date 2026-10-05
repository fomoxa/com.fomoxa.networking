using System;
using Fomoxa.Networking.Timing;

namespace Fomoxa.Unity
{
    public enum TimingMode
    {
        Tick,
        Variable,
    }

    public sealed class TimeManager
    {
        private const double TickRoundingTolerance = 1e-9;

        private readonly int configuredTickRate;
        private double accumulatedTicks;

        internal TimeManager(int tickRate, int maxTicksPerFrame, TimingMode mode)
        {
            if (tickRate < 1 || tickRate > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(tickRate));
            }

            if (maxTicksPerFrame < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxTicksPerFrame));
            }

            configuredTickRate = tickRate;
            TickRate = tickRate;
            MaxTicksPerFrame = maxTicksPerFrame;
            Mode = mode;
        }

        public event Action OnPreTick;

        public event Action OnTick;

        public event Action OnPostTick;

        public int TickRate { get; private set; }

        public double TickDelta => 1.0 / TickRate;

        public int MaxTicksPerFrame { get; }

        public TimingMode Mode { get; }

        public uint Tick { get; private set; }

        public uint ServerTick => FollowedClock?.ServerTick ?? Tick;

        public uint PredictionTick
        {
            get
            {
                ClockEstimator clock = FollowedClock;
                if (clock == null)
                {
                    return Tick;
                }

                return Mode == TimingMode.Variable ? clock.ServerTick : clock.PredictionTick;
            }
        }

        public bool ClockSynced => FollowedClock?.Synced ?? true;

        internal ClockEstimator Clock { get; set; }

        internal Func<bool> FollowsClock { get; set; }

        private ClockEstimator FollowedClock => Clock != null && FollowsClock != null && FollowsClock() ? Clock : null;

        internal void UseTickRate(int tickRate)
        {
            if (tickRate < 1 || tickRate > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(tickRate));
            }

            TickRate = tickRate;
        }

        internal void RestoreTickRate() => TickRate = configuredTickRate;

        internal int Advance(double unscaledDeltaSeconds)
        {
            ClockEstimator clock = FollowedClock;
            double scale = clock != null && clock.Synced && Mode == TimingMode.Tick ? clock.TickScale : 1;
            accumulatedTicks += unscaledDeltaSeconds * TickRate / scale;
            double dueTicks = Math.Floor(accumulatedTicks + TickRoundingTolerance);
            accumulatedTicks = Math.Max(0, accumulatedTicks - dueTicks);
            return (int)Math.Min(dueTicks, MaxTicksPerFrame);
        }

        internal void RaisePreTick(TimeSpan now)
        {
            Tick++;
            FollowedClock?.Advance(now);
            OnPreTick?.Invoke();
        }

        internal void RaiseTick()
        {
            OnTick?.Invoke();
        }

        internal void RaisePostTick()
        {
            OnPostTick?.Invoke();
        }
    }
}
