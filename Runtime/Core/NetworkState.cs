using System;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;

namespace Fomoxa.Networking
{
    public sealed class NetworkState
    {
        private readonly EntityBehaviour behaviour;
        private readonly MessageChannels channels;

        internal NetworkState(EntityBehaviour behaviour, MessageChannels channels, StateProtocol protocol)
        {
            this.behaviour = behaviour;
            this.channels = channels;
            Protocol = protocol;
        }

        internal StateProtocol Protocol { get; }

        public void Use<T>(IMessageCodec<T> codec, T instance, Action<T> onChanged = null, bool onlyWhenDirty = false)
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

            string owner = behaviour.DeclaringType.FullName;
            if (!channels.IsReliable(codec.MessageId))
            {
                throw new HandlerRegistrationException($"{owner} uses message id 0x{codec.MessageId:X8} as its state, which is not on the reliable-ordered channel; declare the model's codec with [NetworkChannel(..., Channel.ReliableOrdered)]");
            }

            if (codec.Encode(instance).IsEmpty)
            {
                throw new HandlerRegistrationException($"{owner} uses message id 0x{codec.MessageId:X8} as its state, whose model encodes to no bytes; a state model needs at least one field");
            }

            behaviour.SetState(new StateSlot<T>(codec, instance, onChanged, onlyWhenDirty));
        }

        public void Use<T>(byte index, NetworkList<T> list, IMessageCodec<T> codec, NetworkListChanged<T> onChanged = null)
            where T : class, new()
        {
            Bind(list, new ListSlot<T>(behaviour, index, Checked(list, nameof(list)), ModelOf(codec), onChanged));
        }

        public void Use<T>(byte index, NetworkList<T> list, NetworkListChanged<T> onChanged = null)
        {
            Bind(list, new ListSlot<T>(behaviour, index, Checked(list, nameof(list)), ValueOf<T>(index, "elements"), onChanged));
        }

        public void Use<TKey, TValue>(byte index, NetworkDictionary<TKey, TValue> dictionary, IMessageCodec<TValue> codec, NetworkDictionaryChanged<TKey, TValue> onChanged = null)
            where TValue : class, new()
        {
            Bind(dictionary, new DictionarySlot<TKey, TValue>(behaviour, index, Checked(dictionary, nameof(dictionary)), ValueOf<TKey>(index, "keys"), ModelOf(codec), onChanged));
        }

        public void Use<TKey, TValue>(byte index, NetworkDictionary<TKey, TValue> dictionary, NetworkDictionaryChanged<TKey, TValue> onChanged = null)
        {
            Bind(dictionary, new DictionarySlot<TKey, TValue>(behaviour, index, Checked(dictionary, nameof(dictionary)), ValueOf<TKey>(index, "keys"), ValueOf<TValue>(index, "values"), onChanged));
        }

        public void Use<T>(byte index, NetworkSet<T> set, NetworkSetChanged<T> onChanged = null)
        {
            Bind(set, new SetSlot<T>(behaviour, index, Checked(set, nameof(set)), ValueOf<T>(index, "elements"), onChanged));
        }

        private static TCollection Checked<TCollection>(TCollection collection, string name)
            where TCollection : class
        {
            if (collection == null)
            {
                throw new ArgumentNullException(name);
            }

            return collection;
        }

        private static CollectionElement<T> ModelOf<T>(IMessageCodec<T> codec)
            where T : class, new()
        {
            if (codec == null)
            {
                throw new ArgumentNullException(nameof(codec));
            }

            return new ModelElement<T>(codec);
        }

        private CollectionElement<T> ValueOf<T>(byte index, string role)
        {
            if (!Protocol.Collections.TryGetValue(out CollectionElement<T> element))
            {
                throw new HandlerRegistrationException($"{behaviour.DeclaringType.FullName} uses {typeof(T).FullName} as the {role} of collection {index}, which is not a primitive type; a model needs its codec, and keys and set elements must be primitive");
            }

            return element;
        }

        private void Bind<T>(NetworkList<T> list, ListSlot<T> slot)
        {
            EnsureUnbound(list.Slot, slot.Index);
            behaviour.AddCollection(slot);
            list.Slot = slot;
        }

        private void Bind<TKey, TValue>(NetworkDictionary<TKey, TValue> dictionary, DictionarySlot<TKey, TValue> slot)
        {
            EnsureUnbound(dictionary.Slot, slot.Index);
            behaviour.AddCollection(slot);
            dictionary.Slot = slot;
        }

        private void Bind<T>(NetworkSet<T> set, SetSlot<T> slot)
        {
            EnsureUnbound(set.Slot, slot.Index);
            behaviour.AddCollection(slot);
            set.Slot = slot;
        }

        private void EnsureUnbound(CollectionSlot bound, byte index)
        {
            if (bound != null)
            {
                throw new HandlerRegistrationException($"{behaviour.DeclaringType.FullName} uses as collection {index} an instance that is already collection {bound.Index} of a behaviour");
            }
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

        protected StateSlot(uint messageId, bool onlyWhenDirty)
        {
            MessageId = messageId;
            OnlyWhenDirty = onlyWhenDirty;
        }

        public uint MessageId { get; }

        public bool OnlyWhenDirty { get; }

        public bool Dirty { get; private set; }

        public bool NeedsCapture => !OnlyWhenDirty || Dirty;

        public EntityRecord Record { get; set; }

        public ReadOnlyMemory<byte> Sent => new ReadOnlyMemory<byte>(sent, 0, sentLength);

        public bool HasDelta { get; private set; }

        public ReadOnlyMemory<byte> Delta => new ReadOnlyMemory<byte>(delta, 0, deltaLength);

        public bool OutOfSync { get; private set; }

        private ReadOnlyMemory<byte> Received => new ReadOnlyMemory<byte>(received, 0, receivedLength);

        public void MarkDirty()
        {
            Dirty = true;
            if (OnlyWhenDirty)
            {
                Record?.Server?.QueueState(Record);
            }
        }

        public bool Capture()
        {
            Dirty = false;
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

        public StateSlot(IMessageCodec<T> codec, T instance, Action<T> onChanged, bool onlyWhenDirty)
            : base(codec.MessageId, onlyWhenDirty)
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
