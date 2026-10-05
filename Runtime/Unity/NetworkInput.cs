using System;
using System.Collections.Generic;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Unity
{
    public readonly struct InputContext
    {
        public InputContext(uint tick, bool repeated, bool replaying = false, uint repeatedTicks = 0)
        {
            Tick = tick;
            Repeated = repeated;
            Replaying = replaying;
            RepeatedTicks = repeated ? repeatedTicks : 0;
        }

        public uint Tick { get; }

        public bool Repeated { get; }

        public bool Replaying { get; }

        public uint RepeatedTicks { get; }
    }

    public sealed class NetworkInput
    {
        private readonly NetworkBehaviour behaviour;
        private readonly InputRules rules;

        internal NetworkInput(NetworkBehaviour behaviour, InputRules rules)
        {
            this.behaviour = behaviour;
            this.rules = rules;
        }

        public void Use<T>(IMessageCodec<T> codec, Action<T> gather, Action<T, InputContext> apply)
            where T : class, new()
        {
            if (codec == null)
            {
                throw new ArgumentNullException(nameof(codec));
            }

            if (gather == null)
            {
                throw new ArgumentNullException(nameof(gather));
            }

            if (apply == null)
            {
                throw new ArgumentNullException(nameof(apply));
            }

            if (!rules.Allowed)
            {
                throw new HandlerRegistrationException($"{behaviour.GetType().FullName} registers an input model, but its NetworkManager uses the Variable timing mode; prediction needs the Tick timing mode");
            }

            behaviour.SetInput(new InputSlot<T>(codec, gather, apply, rules.Redundancy, rules.History));
        }

        public void Reconcile<TState>(IMessageCodec<TState> codec, Action<TState> capture, Action<TState> restore, Func<TState, TState, bool> matches = null)
            where TState : class, new()
        {
            if (codec == null)
            {
                throw new ArgumentNullException(nameof(codec));
            }

            if (capture == null)
            {
                throw new ArgumentNullException(nameof(capture));
            }

            if (restore == null)
            {
                throw new ArgumentNullException(nameof(restore));
            }

            InputSlot slot = behaviour.InputSlot;
            if (slot == null)
            {
                throw new HandlerRegistrationException($"{behaviour.GetType().FullName} registers a reconcile model before its input model; call input.Use first");
            }

            if (slot.Reconcile != null)
            {
                throw new HandlerRegistrationException($"{behaviour.GetType().FullName} uses more than one reconcile model");
            }

            slot.Reconcile = new ReconcileSlot<TState>(codec, capture, restore, matches);
        }
    }

    internal sealed class InputRules
    {
        private int redundancy = 3;
        private int history = 64;
        private int reconcileInterval = 1;

        public bool Allowed { get; set; }

        public int Redundancy
        {
            get => redundancy;
            set => redundancy = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), "an input frame carries at least one input");
        }

        public int History
        {
            get => history;
            set => history = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), "the prediction history keeps at least one tick");
        }

        public int ReconcileInterval
        {
            get => reconcileInterval;
            set => reconcileInterval = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), "the reconcile interval is at least one tick");
        }
    }

    internal abstract class ReconcileSlot
    {
        public abstract ReadOnlyMemory<byte> Capture();

        public abstract void Restore(ReadOnlyMemory<byte> state);

        public abstract bool Matches(ReadOnlyMemory<byte> server, ReadOnlyMemory<byte> predicted);
    }

    internal sealed class ReconcileSlot<TState> : ReconcileSlot
        where TState : class, new()
    {
        private readonly IMessageCodec<TState> codec;
        private readonly Action<TState> capture;
        private readonly Action<TState> restore;
        private readonly Func<TState, TState, bool> matches;
        private TState instance = new TState();
        private TState compared = new TState();

        public ReconcileSlot(IMessageCodec<TState> codec, Action<TState> capture, Action<TState> restore, Func<TState, TState, bool> matches)
        {
            this.codec = codec;
            this.capture = capture;
            this.restore = restore;
            this.matches = matches;
        }

        public override ReadOnlyMemory<byte> Capture()
        {
            capture(instance);
            return codec.Encode(instance);
        }

        public override void Restore(ReadOnlyMemory<byte> state)
        {
            codec.Decode(state, ref instance);
            restore(instance);
        }

        public override bool Matches(ReadOnlyMemory<byte> server, ReadOnlyMemory<byte> predicted)
        {
            if (matches == null)
            {
                return server.Span.SequenceEqual(predicted.Span);
            }

            codec.Decode(server, ref instance);
            codec.Decode(predicted, ref compared);
            return matches(instance, compared);
        }
    }

    internal abstract class InputSlot
    {
        private readonly int redundancy;
        private readonly Entry[] entries;
        private uint newestTick;
        private int newest = -1;
        private int count;
        private byte[] pendingState = Array.Empty<byte>();
        private int pendingLength;

        protected InputSlot(int redundancy, int history)
        {
            this.redundancy = redundancy;
            entries = new Entry[Math.Max(redundancy, history)];
            for (int index = 0; index < entries.Length; index++)
            {
                entries[index] = new Entry();
            }
        }

        public ReconcileSlot Reconcile { get; set; }

        public bool Predicting { get; set; }

        public bool HasPending { get; private set; }

        public uint PendingTick { get; private set; }

        public int HistoryCount => count;

        public uint NewestTick => newestTick;

        public abstract ReadOnlyMemory<byte> Gather();

        public abstract void Apply(ReadOnlyMemory<byte> input, InputContext context);

        public abstract void ApplyGathered(InputContext context);

        public abstract void GatherAndApply(InputContext context);

        public void Record(uint tick, ReadOnlySpan<byte> input)
        {
            if (count > 0 && tick != newestTick + 1)
            {
                ClearHistory();
            }

            newest = (newest + 1) % entries.Length;
            entries[newest].Fill(tick, input);
            newestTick = tick;
            count = Math.Min(count + 1, entries.Length);
        }

        public void CopyFramesTo(List<ReadOnlyMemory<byte>> frames)
        {
            frames.Clear();
            for (int offset = 0; offset < Math.Min(count, redundancy); offset++)
            {
                frames.Add(At(offset).Input);
            }
        }

        public void StoreState(uint tick, ReadOnlySpan<byte> state)
        {
            if (TryFind(tick, out Entry entry))
            {
                entry.SetState(state);
            }
        }

        public void HoldPending(uint tick, ReadOnlySpan<byte> state)
        {
            if (HasPending && tick <= PendingTick)
            {
                return;
            }

            if (pendingState.Length < state.Length)
            {
                pendingState = new byte[state.Length];
            }

            state.CopyTo(pendingState);
            pendingLength = state.Length;
            PendingTick = tick;
            HasPending = true;
        }

        public bool ReconcilePending()
        {
            ReadOnlyMemory<byte> server = TakePending(out uint tick);
            bool matched = MatchesAt(tick, server);
            if (!matched)
            {
                Reconcile.Restore(server);
                Replay(tick);
            }

            DropThrough(tick);
            return !matched;
        }

        public ReadOnlyMemory<byte> TakePending(out uint tick)
        {
            HasPending = false;
            tick = PendingTick;
            return new ReadOnlyMemory<byte>(pendingState, 0, pendingLength);
        }

        public bool MatchesAt(uint tick, ReadOnlyMemory<byte> server) =>
            Predicting
            && TryFind(tick, out Entry entry)
            && entry.HasState
            && Reconcile.Matches(server, entry.State);

        public void ReplayInput(uint tick)
        {
            if (count == 0 || tick > newestTick)
            {
                return;
            }

            uint oldestTick = newestTick - (uint)(count - 1);
            bool repeated = tick < oldestTick;
            Entry source = repeated ? At(count - 1) : At((int)(newestTick - tick));
            Apply(source.Input, new InputContext(tick, repeated, true, repeated ? oldestTick - tick : 0));
        }

        public void CaptureAt(uint tick) => StoreState(tick, Reconcile.Capture().Span);

        public void Forget(uint tick) => DropThrough(tick);

        public void ClearHistory()
        {
            count = 0;
            newest = -1;
            HasPending = false;
        }

        private void Replay(uint tick)
        {
            if (count == 0)
            {
                return;
            }

            uint oldestTick = newestTick - (uint)(count - 1);
            for (uint replayed = tick + 1; replayed <= newestTick && replayed > tick; replayed++)
            {
                bool repeated;
                Entry source;
                if (replayed >= oldestTick)
                {
                    source = At((int)(newestTick - replayed));
                    repeated = false;
                }
                else
                {
                    source = At(count - 1);
                    repeated = true;
                }

                Apply(source.Input, new InputContext(replayed, repeated, true, repeated ? oldestTick - replayed : 0));
                if (!repeated)
                {
                    source.SetState(Reconcile.Capture().Span);
                }
            }
        }

        private void DropThrough(uint tick)
        {
            while (count > redundancy && newestTick - (uint)(count - 1) <= tick)
            {
                count--;
            }
        }

        private bool TryFind(uint tick, out Entry entry)
        {
            if (count == 0 || tick > newestTick || newestTick - tick >= (uint)count)
            {
                entry = null;
                return false;
            }

            entry = At((int)(newestTick - tick));
            return true;
        }

        private Entry At(int offset) => entries[(newest - offset + entries.Length) % entries.Length];

        private sealed class Entry
        {
            private byte[] input = Array.Empty<byte>();
            private int inputLength;
            private byte[] state = Array.Empty<byte>();
            private int stateLength;

            public uint Tick { get; private set; }

            public bool HasState { get; private set; }

            public ReadOnlyMemory<byte> Input => new ReadOnlyMemory<byte>(input, 0, inputLength);

            public ReadOnlyMemory<byte> State => new ReadOnlyMemory<byte>(state, 0, stateLength);

            public void Fill(uint tick, ReadOnlySpan<byte> value)
            {
                Tick = tick;
                HasState = false;
                if (input.Length < value.Length)
                {
                    input = new byte[value.Length];
                }

                value.CopyTo(input);
                inputLength = value.Length;
            }

            public void SetState(ReadOnlySpan<byte> value)
            {
                if (state.Length < value.Length)
                {
                    state = new byte[value.Length];
                }

                value.CopyTo(state);
                stateLength = value.Length;
                HasState = true;
            }
        }
    }

    internal sealed class InputSlot<T> : InputSlot
        where T : class, new()
    {
        private readonly IMessageCodec<T> codec;
        private readonly Action<T> gather;
        private readonly Action<T, InputContext> apply;
        private T instance = new T();

        public InputSlot(IMessageCodec<T> codec, Action<T> gather, Action<T, InputContext> apply, int redundancy, int history)
            : base(redundancy, history)
        {
            this.codec = codec;
            this.gather = gather;
            this.apply = apply;
        }

        public override ReadOnlyMemory<byte> Gather()
        {
            gather(instance);
            return codec.Encode(instance);
        }

        public override void Apply(ReadOnlyMemory<byte> input, InputContext context)
        {
            codec.Decode(input, ref instance);
            apply(instance, context);
        }

        public override void ApplyGathered(InputContext context) => apply(instance, context);

        public override void GatherAndApply(InputContext context)
        {
            gather(instance);
            apply(instance, context);
        }
    }
}
