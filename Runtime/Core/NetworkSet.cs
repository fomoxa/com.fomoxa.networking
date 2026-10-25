using System;
using System.Collections;
using System.Collections.Generic;

namespace Fomoxa.Networking
{
    public sealed class NetworkSet<T> : IReadOnlyCollection<T>
    {
        private readonly HashSet<T> items = new HashSet<T>();

        public int Count => items.Count;

        internal HashSet<T> Items => items;

        internal SetSlot<T> Slot { get; set; }

        public bool Add(T item)
        {
            NetworkCollections.EnsureNotNull(item, nameof(item));
            bool recording = Recording();
            if (!items.Add(item))
            {
                return false;
            }

            if (recording)
            {
                Slot.RecordChange(ChangeKind.Add, item);
            }

            return true;
        }

        public bool Remove(T item)
        {
            NetworkCollections.EnsureNotNull(item, nameof(item));
            bool recording = Recording();
            if (!items.Remove(item))
            {
                return false;
            }

            if (recording)
            {
                Slot.RecordChange(ChangeKind.Remove, item);
            }

            return true;
        }

        public void Clear()
        {
            bool recording = Recording();
            items.Clear();
            if (recording)
            {
                Slot.RecordClear();
            }
        }

        public bool Contains(T item) => items.Contains(item);

        public HashSet<T>.Enumerator GetEnumerator() => items.GetEnumerator();

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();

        private bool Recording() => Slot != null && Slot.Recording();
    }

    internal sealed class SetSlot<T> : CollectionSlot
    {
        private readonly NetworkSet<T> set;
        private readonly CollectionElement<T> element;
        private readonly NetworkSetChanged<T> onChanged;
        private readonly HashSet<T> staged = new HashSet<T>();
        private HashSet<T> mirror;

        public SetSlot(EntityBehaviour behaviour, byte index, NetworkSet<T> set, CollectionElement<T> element, NetworkSetChanged<T> onChanged)
            : base(behaviour, index, CollectionKind.Set, 0, element.MessageId)
        {
            this.set = set;
            this.element = element;
            this.onChanged = onChanged;
        }

        public void RecordChange(ChangeKind kind, T item)
        {
            if (Begin(kind))
            {
                WriteValue(element.Encode(item));
            }
        }

        public void RecordClear()
        {
            Begin(ChangeKind.Clear);
        }

        protected override bool Carries(ChangeKind kind, out bool index, out bool key, out bool value)
        {
            index = false;
            key = false;
            value = kind == ChangeKind.Add || kind == ChangeKind.Remove;
            return value || kind == ChangeKind.Clear;
        }

        protected override void Apply(ChangeKind kind, uint index, ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, bool shared)
        {
            HashSet<T> target = Target(shared);
            if (kind == ChangeKind.Clear)
            {
                target.Clear();
                onChanged?.Invoke(NetworkSetOperation.Clear, default, default);
                return;
            }

            T item = element.Decode(value);
            if (kind == ChangeKind.Add)
            {
                if (!target.Add(item))
                {
                    throw Unappliable($"collection {Index} already has the element it adds");
                }

                onChanged?.Invoke(NetworkSetOperation.Add, default, item);
                return;
            }

            if (!target.Remove(item))
            {
                throw Unappliable($"collection {Index} has no element to remove");
            }

            onChanged?.Invoke(NetworkSetOperation.Remove, item, default);
        }

        protected override void WriteContent(ByteSegments keys, ByteSegments values)
        {
            foreach (T item in set.Items)
            {
                values.Add(element.Encode(item).Span);
            }
        }

        protected override void StageContent(List<ReadOnlyMemory<byte>> keys, List<ReadOnlyMemory<byte>> values)
        {
            if (keys.Count != 0)
            {
                throw Unappliable($"collection {Index} is a set and carries no keys");
            }

            staged.Clear();
            foreach (ReadOnlyMemory<byte> value in values)
            {
                if (!staged.Add(element.Decode(value)))
                {
                    throw Unappliable($"collection {Index} carries an element twice");
                }
            }
        }

        protected override void CommitContent(bool shared, bool reset)
        {
            HashSet<T> target = Target(shared);
            target.Clear();
            target.UnionWith(staged);
            staged.Clear();
            if (reset)
            {
                onChanged?.Invoke(NetworkSetOperation.Reset, default, default);
            }
        }

        private HashSet<T> Target(bool shared) => shared ? mirror ??= new HashSet<T>() : set.Items;
    }
}
