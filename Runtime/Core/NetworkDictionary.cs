using System;
using System.Collections;
using System.Collections.Generic;

namespace Fomoxa.Networking
{
    public sealed class NetworkDictionary<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>
    {
        private readonly Dictionary<TKey, TValue> entries = new Dictionary<TKey, TValue>();

        public int Count => entries.Count;

        public Dictionary<TKey, TValue>.KeyCollection Keys => entries.Keys;

        public Dictionary<TKey, TValue>.ValueCollection Values => entries.Values;

        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => entries.Keys;

        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => entries.Values;

        internal Dictionary<TKey, TValue> Entries => entries;

        internal DictionarySlot<TKey, TValue> Slot { get; set; }

        public TValue this[TKey key]
        {
            get => entries[key];
            set
            {
                NetworkCollections.EnsureNotNull(key, nameof(key));
                NetworkCollections.EnsureNotNull(value, nameof(value));
                bool recording = Recording();
                bool existed = entries.ContainsKey(key);
                entries[key] = value;
                if (recording)
                {
                    Slot.RecordPut(existed, key, value);
                }
            }
        }

        public void Add(TKey key, TValue value)
        {
            NetworkCollections.EnsureNotNull(key, nameof(key));
            NetworkCollections.EnsureNotNull(value, nameof(value));
            bool recording = Recording();
            entries.Add(key, value);
            if (recording)
            {
                Slot.RecordPut(false, key, value);
            }
        }

        public bool Remove(TKey key)
        {
            NetworkCollections.EnsureNotNull(key, nameof(key));
            bool recording = Recording();
            if (!entries.Remove(key))
            {
                return false;
            }

            if (recording)
            {
                Slot.RecordRemove(key);
            }

            return true;
        }

        public void Clear()
        {
            bool recording = Recording();
            entries.Clear();
            if (recording)
            {
                Slot.RecordClear();
            }
        }

        public void Dirty(TKey key)
        {
            bool recording = Recording();
            TValue value = entries[key];
            if (recording)
            {
                Slot.RecordPut(true, key, value);
            }
        }

        public bool ContainsKey(TKey key) => entries.ContainsKey(key);

        public bool TryGetValue(TKey key, out TValue value) => entries.TryGetValue(key, out value);

        public Dictionary<TKey, TValue>.Enumerator GetEnumerator() => entries.GetEnumerator();

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => entries.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => entries.GetEnumerator();

        internal void ModifyCore<TState>(TKey key, TState state, Action<TValue, TState> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            bool recording = Recording();
            TValue value = entries[key];
            try
            {
                action(value, state);
            }
            finally
            {
                if (recording)
                {
                    Slot.RecordPut(true, key, value);
                }
            }
        }

        private bool Recording() => Slot != null && Slot.Recording();
    }

    internal sealed class DictionarySlot<TKey, TValue> : CollectionSlot
    {
        private readonly NetworkDictionary<TKey, TValue> dictionary;
        private readonly CollectionElement<TKey> keyElement;
        private readonly CollectionElement<TValue> valueElement;
        private readonly NetworkDictionaryChanged<TKey, TValue> onChanged;
        private readonly Dictionary<TKey, TValue> staged = new Dictionary<TKey, TValue>();
        private Dictionary<TKey, TValue> mirror;

        public DictionarySlot(EntityBehaviour behaviour, byte index, NetworkDictionary<TKey, TValue> dictionary, CollectionElement<TKey> keyElement, CollectionElement<TValue> valueElement, NetworkDictionaryChanged<TKey, TValue> onChanged)
            : base(behaviour, index, CollectionKind.Dictionary, keyElement.MessageId, valueElement.MessageId)
        {
            this.dictionary = dictionary;
            this.keyElement = keyElement;
            this.valueElement = valueElement;
            this.onChanged = onChanged;
        }

        public void RecordPut(bool existed, TKey key, TValue value)
        {
            if (Begin(existed ? ChangeKind.Set : ChangeKind.Add))
            {
                WriteKey(keyElement.Encode(key));
                WriteValue(valueElement.Encode(value));
            }
        }

        public void RecordRemove(TKey key)
        {
            if (Begin(ChangeKind.Remove))
            {
                WriteKey(keyElement.Encode(key));
            }
        }

        public void RecordClear()
        {
            Begin(ChangeKind.Clear);
        }

        protected override bool Carries(ChangeKind kind, out bool index, out bool key, out bool value)
        {
            index = false;
            key = kind == ChangeKind.Add || kind == ChangeKind.Set || kind == ChangeKind.Remove;
            value = kind == ChangeKind.Add || kind == ChangeKind.Set;
            return key || kind == ChangeKind.Clear;
        }

        protected override void Apply(ChangeKind kind, uint index, ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, bool shared)
        {
            Dictionary<TKey, TValue> target = Target(shared);
            if (kind == ChangeKind.Clear)
            {
                target.Clear();
                onChanged?.Invoke(NetworkDictionaryOperation.Clear, default, default, default);
                return;
            }

            TKey decodedKey = keyElement.Decode(key);
            bool exists = target.TryGetValue(decodedKey, out TValue old);
            switch (kind)
            {
                case ChangeKind.Add:
                {
                    if (exists)
                    {
                        throw Unappliable($"collection {Index} already has the key it adds");
                    }

                    TValue added = valueElement.Decode(value);
                    target.Add(decodedKey, added);
                    onChanged?.Invoke(NetworkDictionaryOperation.Add, decodedKey, default, added);
                    break;
                }

                case ChangeKind.Set:
                {
                    if (!exists)
                    {
                        throw Unappliable($"collection {Index} has no key to set");
                    }

                    TValue replaced = valueElement.Decode(value);
                    target[decodedKey] = replaced;
                    onChanged?.Invoke(NetworkDictionaryOperation.Set, decodedKey, old, replaced);
                    break;
                }

                default:
                {
                    if (!exists)
                    {
                        throw Unappliable($"collection {Index} has no key to remove");
                    }

                    target.Remove(decodedKey);
                    onChanged?.Invoke(NetworkDictionaryOperation.Remove, decodedKey, old, default);
                    break;
                }
            }
        }

        protected override void WriteContent(ByteSegments keys, ByteSegments values)
        {
            foreach (KeyValuePair<TKey, TValue> entry in dictionary.Entries)
            {
                keys.Add(keyElement.Encode(entry.Key).Span);
                values.Add(valueElement.Encode(entry.Value).Span);
            }
        }

        protected override void StageContent(List<ReadOnlyMemory<byte>> keys, List<ReadOnlyMemory<byte>> values)
        {
            if (keys.Count != values.Count)
            {
                throw Unappliable($"collection {Index} carries {keys.Count} keys for {values.Count} values");
            }

            staged.Clear();
            for (int at = 0; at < keys.Count; at++)
            {
                if (!staged.TryAdd(keyElement.Decode(keys[at]), valueElement.Decode(values[at])))
                {
                    throw Unappliable($"collection {Index} carries a key twice");
                }
            }
        }

        protected override void CommitContent(bool shared, bool reset)
        {
            Dictionary<TKey, TValue> target = Target(shared);
            target.Clear();
            foreach (KeyValuePair<TKey, TValue> entry in staged)
            {
                target.Add(entry.Key, entry.Value);
            }

            staged.Clear();
            if (reset)
            {
                onChanged?.Invoke(NetworkDictionaryOperation.Reset, default, default, default);
            }
        }

        private Dictionary<TKey, TValue> Target(bool shared) => shared ? mirror ??= new Dictionary<TKey, TValue>() : dictionary.Entries;
    }
}
