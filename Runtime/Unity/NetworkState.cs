using System;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking;

namespace Fomoxa.Unity
{
    public sealed class NetworkState
    {
        private readonly NetworkBehaviour behaviour;
        private readonly MessageChannels channels;

        internal NetworkState(NetworkBehaviour behaviour, MessageChannels channels, StateProtocol protocol)
        {
            this.behaviour = behaviour;
            this.channels = channels;
            Protocol = protocol;
        }

        internal StateProtocol Protocol { get; }

        public void Use<T>(IMessageCodec<T> codec, T instance, Action<T> onChanged = null)
            where T : class, new()
        {
            if (codec == null)
            {
                throw new ArgumentNullException(nameof(codec));
            }

            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            string owner = behaviour.GetType().FullName;
            if (!channels.IsReliable(codec.MessageId))
            {
                throw new HandlerRegistrationException($"{owner} uses message id 0x{codec.MessageId:X8} as its state, which is not on the reliable-ordered channel; declare the model's codec with [NetworkChannel(..., Channel.ReliableOrdered)]");
            }

            if (codec.Encode(instance).IsEmpty)
            {
                throw new HandlerRegistrationException($"{owner} uses message id 0x{codec.MessageId:X8} as its state, whose model encodes to no bytes; a state model needs at least one field");
            }

            behaviour.SetState(new StateSlot<T>(codec, instance, onChanged));
        }
    }

    internal abstract class StateSlot
    {
        private byte[] sent = Array.Empty<byte>();
        private int sentLength;
        private byte[] received = Array.Empty<byte>();
        private int receivedLength;
        private byte[] pending = Array.Empty<byte>();
        private int pendingLength;
        private byte[] delta = Array.Empty<byte>();
        private int deltaLength;

        protected StateSlot(uint messageId)
        {
            MessageId = messageId;
        }

        public uint MessageId { get; }

        public ReadOnlyMemory<byte> Sent => new ReadOnlyMemory<byte>(sent, 0, sentLength);

        public bool HasDelta { get; private set; }

        public ReadOnlyMemory<byte> Delta => new ReadOnlyMemory<byte>(delta, 0, deltaLength);

        public bool OutOfSync { get; private set; }

        private ReadOnlyMemory<byte> Received => new ReadOnlyMemory<byte>(received, 0, receivedLength);

        public bool Capture()
        {
            ReadOnlySpan<byte> current = Encode().Span;
            ReadOnlySpan<byte> previous = Sent.Span;
            if (current.SequenceEqual(previous))
            {
                return false;
            }

            HasDelta = sentLength > 0 && current.Length == sentLength;
            if (HasDelta)
            {
                deltaLength = ZeroRuns.Encode(current, previous, ref delta);
            }

            Keep(current, ref sent, ref sentLength);
            return true;
        }

        public void Validate(ReadOnlyMemory<byte> bytes)
        {
            DecodeSpare(bytes);
        }

        public void ApplyInitial(ReadOnlyMemory<byte> bytes, bool shared)
        {
            if (!shared)
            {
                DecodeInstance(bytes);
            }

            Keep(bytes.Span, ref received, ref receivedLength);
            OutOfSync = false;
        }

        public void Receive(ReadOnlyMemory<byte> bytes, bool shared)
        {
            DecodeSpare(bytes);
            if (!shared)
            {
                DecodeInstance(bytes);
            }

            DecodeSpare(Received);
            Keep(bytes.Span, ref received, ref receivedLength);
            OutOfSync = false;
            RaiseChanged();
        }

        public void ReceiveDelta(ReadOnlyMemory<byte> data, bool shared)
        {
            Keep(Received.Span, ref pending, ref pendingLength);
            if (!ZeroRuns.TryApply(data.Span, new Span<byte>(pending, 0, pendingLength)))
            {
                throw new MessageDecodeException($"a state delta of {data.Length} bytes runs past the {pendingLength} bytes of its base", null);
            }

            var next = new ReadOnlyMemory<byte>(pending, 0, pendingLength);
            DecodeSpare(next);
            if (!shared)
            {
                DecodeInstance(next);
            }

            DecodeSpare(Received);
            (received, pending) = (pending, received);
            (receivedLength, pendingLength) = (pendingLength, receivedLength);
            RaiseChanged();
        }

        public void MarkOutOfSync()
        {
            OutOfSync = true;
        }

        protected abstract ReadOnlyMemory<byte> Encode();

        protected abstract void DecodeSpare(ReadOnlyMemory<byte> bytes);

        protected abstract void DecodeInstance(ReadOnlyMemory<byte> bytes);

        protected abstract void RaiseChanged();

        private static void Keep(ReadOnlySpan<byte> bytes, ref byte[] buffer, ref int length)
        {
            if (buffer.Length < bytes.Length)
            {
                buffer = new byte[bytes.Length];
            }

            bytes.CopyTo(buffer);
            length = bytes.Length;
        }
    }

    internal sealed class StateSlot<T> : StateSlot
        where T : class, new()
    {
        private readonly IMessageCodec<T> codec;
        private readonly T instance;
        private readonly Action<T> onChanged;
        private T spare = new T();

        public StateSlot(IMessageCodec<T> codec, T instance, Action<T> onChanged)
            : base(codec.MessageId)
        {
            this.codec = codec;
            this.instance = instance;
            this.onChanged = onChanged;
        }

        protected override ReadOnlyMemory<byte> Encode() => codec.Encode(instance);

        protected override void DecodeSpare(ReadOnlyMemory<byte> bytes)
        {
            codec.Decode(bytes, ref spare);
        }

        protected override void DecodeInstance(ReadOnlyMemory<byte> bytes)
        {
            T target = instance;
            codec.Decode(bytes, ref target);
        }

        protected override void RaiseChanged()
        {
            onChanged?.Invoke(spare);
        }
    }
}
