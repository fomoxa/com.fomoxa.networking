using System;
using System.Collections;
using System.Collections.Generic;

namespace Fomoxa.Networking
{
    public sealed class NetworkList<T> : IReadOnlyList<T>
    {
        private readonly List<T> items = new List<T>();

        public int Count => items.Count;

        internal List<T> Items => items;

        internal ListSlot<T> Slot { get; set; }

        public T this[int index]
        {
            get => items[index];
            set
            {
                NetworkCollections.EnsureNotNull(value, nameof(value));
                bool recording = Recording();
                items[index] = value;
                if (recording)
                {
                    Slot.RecordSet(index, value);
                }
            }
        }

        public void Add(T item)
        {
            NetworkCollections.EnsureNotNull(item, nameof(item));
            bool recording = Recording();
            items.Add(item);
            if (recording)
            {
                Slot.RecordAdd(item);
            }
        }

        public void Insert(int index, T item)
        {
            NetworkCollections.EnsureNotNull(item, nameof(item));
            bool recording = Recording();
            items.Insert(index, item);
            if (recording)
            {
                Slot.RecordInsert(index, item);
            }
        }

        public void RemoveAt(int index)
        {
            bool recording = Recording();
            items.RemoveAt(index);
            if (recording)
            {
                Slot.RecordRemoveAt(index);
            }
        }

        public bool Remove(T item)
        {
            bool recording = Recording();
            int index = items.IndexOf(item);
            if (index < 0)
            {
                return false;
            }

            items.RemoveAt(index);
            if (recording)
            {
                Slot.RecordRemoveAt(index);
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

        public void Dirty(int index)
        {
            bool recording = Recording();
            T item = items[index];
            if (recording)
            {
                Slot.RecordSet(index, item);
            }
        }

        public List<T>.Enumerator GetEnumerator() => items.GetEnumerator();

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();

        internal void ModifyCore<TState>(int index, TState state, Action<T, TState> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            bool recording = Recording();
            T item = items[index];
            try
            {
                action(item, state);
            }
            finally
            {
                if (recording)
                {
                    Slot.RecordSet(index, item);
                }
            }
        }

        private bool Recording() => Slot != null && Slot.Recording();
    }

    internal sealed class ListSlot<T> : CollectionSlot
    {
        private readonly NetworkList<T> list;
        private readonly CollectionElement<T> element;
        private readonly NetworkListChanged<T> onChanged;
        private readonly List<T> staged = new List<T>();
        private List<T> mirror;

        public ListSlot(EntityBehaviour behaviour, byte index, NetworkList<T> list, CollectionElement<T> element, NetworkListChanged<T> onChanged)
            : base(behaviour, index, CollectionKind.List, 0, element.MessageId)
        {
            this.list = list;
            this.element = element;
            this.onChanged = onChanged;
        }

        public void RecordAdd(T item)
        {
            if (Begin(ChangeKind.Add))
            {
                WriteValue(element.Encode(item));
            }
        }

        public void RecordInsert(int index, T item)
        {
            if (Begin(ChangeKind.Insert))
            {
                WriteIndex(index);
                WriteValue(element.Encode(item));
            }
        }

        public void RecordSet(int index, T item)
        {
            if (Begin(ChangeKind.Set))
            {
                WriteIndex(index);
                WriteValue(element.Encode(item));
            }
        }

        public void RecordRemoveAt(int index)
        {
            if (Begin(ChangeKind.Remove))
            {
                WriteIndex(index);
            }
        }

        public void RecordClear()
        {
            Begin(ChangeKind.Clear);
        }

        protected override bool Carries(ChangeKind kind, out bool index, out bool key, out bool value)
        {
            index = kind == ChangeKind.Insert || kind == ChangeKind.Set || kind == ChangeKind.Remove;
            key = false;
            value = kind == ChangeKind.Add || kind == ChangeKind.Insert || kind == ChangeKind.Set;
            return kind == ChangeKind.Add || index || kind == ChangeKind.Clear;
        }

        protected override void Apply(ChangeKind kind, uint index, ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, bool shared)
        {
            List<T> target = Target(shared);
            int count = target.Count;
            switch (kind)
            {
                case ChangeKind.Add:
                {
                    T item = element.Decode(value);
                    target.Add(item);
                    onChanged?.Invoke(NetworkListOperation.Add, count, default, item);
                    break;
                }

                case ChangeKind.Insert:
                {
                    if (index > count)
                    {
                        throw Unappliable($"collection {Index} cannot insert at {index} into {count} elements");
                    }

                    T item = element.Decode(value);
                    target.Insert((int)index, item);
                    onChanged?.Invoke(NetworkListOperation.Insert, (int)index, default, item);
                    break;
                }

                case ChangeKind.Set:
                {
                    if (index >= count)
                    {
                        throw Unappliable($"collection {Index} cannot set element {index} of {count}");
                    }

                    T item = element.Decode(value);
                    T old = target[(int)index];
                    target[(int)index] = item;
                    onChanged?.Invoke(NetworkListOperation.Set, (int)index, old, item);
                    break;
                }

                case ChangeKind.Remove:
                {
                    if (index >= count)
                    {
                        throw Unappliable($"collection {Index} cannot remove element {index} of {count}");
                    }

                    T old = target[(int)index];
                    target.RemoveAt((int)index);
                    onChanged?.Invoke(NetworkListOperation.RemoveAt, (int)index, old, default);
                    break;
                }

                default:
                    target.Clear();
                    onChanged?.Invoke(NetworkListOperation.Clear, -1, default, default);
                    break;
            }
        }

        protected override void WriteContent(ByteSegments keys, ByteSegments values)
        {
            foreach (T item in list.Items)
            {
                values.Add(element.Encode(item).Span);
            }
        }

        protected override void StageContent(List<ReadOnlyMemory<byte>> keys, List<ReadOnlyMemory<byte>> values)
        {
            if (keys.Count != 0)
            {
                throw Unappliable($"collection {Index} is a list and carries no keys");
            }

            staged.Clear();
            foreach (ReadOnlyMemory<byte> value in values)
            {
                staged.Add(element.Decode(value));
            }
        }

        protected override void CommitContent(bool shared, bool reset)
        {
            List<T> target = Target(shared);
            target.Clear();
            target.AddRange(staged);
            staged.Clear();
            if (reset)
            {
                onChanged?.Invoke(NetworkListOperation.Reset, -1, default, default);
            }
        }

        private List<T> Target(bool shared) => shared ? mirror ??= new List<T>() : list.Items;
    }
}
