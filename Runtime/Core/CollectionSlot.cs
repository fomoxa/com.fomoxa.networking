using System;
using System.Collections.Generic;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;

namespace Fomoxa.Networking
{
    internal enum CollectionKind : byte
    {
        List = 1,
        Dictionary = 2,
        Set = 3,
    }

    internal enum ChangeKind : byte
    {
        Add = 1,
        Insert = 2,
        Set = 3,
        Remove = 4,
        Clear = 5,
    }

    internal abstract class CollectionSlot
    {
        private readonly EntityBehaviour behaviour;
        private readonly List<uint> pendingIndexes = new List<uint>();
        private readonly ByteSegments pendingKeys = new ByteSegments();
        private readonly ByteSegments pendingValues = new ByteSegments();
        private readonly List<ReadOnlyMemory<byte>> sentKeys = new List<ReadOnlyMemory<byte>>();
        private readonly List<ReadOnlyMemory<byte>> sentValues = new List<ReadOnlyMemory<byte>>();
        private readonly ByteSegments contentKeys = new ByteSegments();
        private readonly ByteSegments contentValues = new ByteSegments();
        private readonly CollectionContent content = new CollectionContent();
        private byte[] pendingKinds = Array.Empty<byte>();
        private int pendingCount;
        private uint firstPending;
        private uint stagedSequence;

        protected CollectionSlot(EntityBehaviour behaviour, byte index, CollectionKind kind, uint keyId, uint valueId)
        {
            this.behaviour = behaviour;
            Index = index;
            Kind = kind;
            KeyId = keyId;
            ValueId = valueId;
        }

        public byte Index { get; }

        public CollectionKind Kind { get; }

        public uint KeyId { get; }

        public uint ValueId { get; }

        public EntityRecord Record { get; set; }

        public uint Sequence { get; private set; }

        public uint Applied { get; private set; }

        public bool OutOfSync { get; private set; }

        public bool HasPending => pendingCount > 0;

        public bool Recording()
        {
            if (Record != null && Record.Server != null)
            {
                return true;
            }

            if (behaviour.SpawnedOnClient)
            {
                throw new InvalidOperationException($"{behaviour.DisplayName} cannot change collection {Index} on a client; only the server changes a collection");
            }

            return false;
        }

        public void FillChanges(CollectionChanges changes)
        {
            changes.Collection = Index;
            changes.First = firstPending;
            changes.Kinds = new ReadOnlyMemory<byte>(pendingKinds, 0, pendingCount);
            changes.Indexes = pendingIndexes;
            pendingKeys.CopyTo(sentKeys);
            pendingValues.CopyTo(sentValues);
            changes.Keys = sentKeys;
            changes.Values = sentValues;
        }

        public void ClearPending()
        {
            pendingCount = 0;
            pendingIndexes.Clear();
            pendingKeys.Clear();
            pendingValues.Clear();
            sentKeys.Clear();
            sentValues.Clear();
        }

        public CollectionContent Content()
        {
            content.Collection = Index;
            content.Kind = (byte)Kind;
            content.KeyId = KeyId;
            content.ValueId = ValueId;
            content.Sequence = Sequence;
            contentKeys.Clear();
            contentValues.Clear();
            WriteContent(contentKeys, contentValues);
            contentKeys.CopyTo(content.Keys);
            contentValues.CopyTo(content.Values);
            return content;
        }

        public bool Matches(CollectionContent received) =>
            received.Collection == Index && received.Kind == (byte)Kind && received.KeyId == KeyId && received.ValueId == ValueId;

        public void Stage(CollectionContent received)
        {
            if (!Matches(received))
            {
                throw new MessageDecodeException($"collection {received.Collection} of kind {received.Kind} with key 0x{received.KeyId:X8} and value 0x{received.ValueId:X8} does not match collection {Index}", null);
            }

            StageContent(received.Keys, received.Values);
            stagedSequence = received.Sequence;
        }

        public void Commit(bool shared, bool reset)
        {
            CommitContent(shared, reset);
            Applied = stagedSequence;
            OutOfSync = false;
        }

        public void Receive(CollectionChanges changes, bool shared)
        {
            ReadOnlySpan<byte> kinds = changes.Kinds.Span;
            int indexCount = 0;
            int keyCount = 0;
            int valueCount = 0;
            foreach (byte kind in kinds)
            {
                if (!Carries((ChangeKind)kind, out bool index, out bool key, out bool value))
                {
                    throw new MessageDecodeException($"change kind {kind} does not apply to collection {Index}", null);
                }

                indexCount += index ? 1 : 0;
                keyCount += key ? 1 : 0;
                valueCount += value ? 1 : 0;
            }

            if (indexCount != changes.Indexes.Count || keyCount != changes.Keys.Count || valueCount != changes.Values.Count)
            {
                throw new MessageDecodeException($"collection {Index} carries {changes.Indexes.Count} indexes, {changes.Keys.Count} keys and {changes.Values.Count} values for changes that need {indexCount}, {keyCount} and {valueCount}", null);
            }

            int nextIndex = 0;
            int nextKey = 0;
            int nextValue = 0;
            uint sequence = changes.First;
            for (int at = 0; at < kinds.Length; at++, sequence++)
            {
                var kind = (ChangeKind)kinds[at];
                Carries(kind, out bool index, out bool key, out bool value);
                uint position = index ? changes.Indexes[nextIndex++] : 0;
                ReadOnlyMemory<byte> keyBytes = key ? changes.Keys[nextKey++] : default;
                ReadOnlyMemory<byte> valueBytes = value ? changes.Values[nextValue++] : default;
                if ((int)(sequence - Applied) <= 0)
                {
                    continue;
                }

                Apply(kind, position, keyBytes, valueBytes, shared);
                Applied = sequence;
            }
        }

        public void MarkOutOfSync()
        {
            OutOfSync = true;
        }

        protected bool Begin(ChangeKind kind)
        {
            Sequence++;
            ServerEntities server = Record.Server;
            bool observed = server.Objects.ObserversOf(Record.ObjectId).Count > 0;
            if (kind == ChangeKind.Clear || !observed)
            {
                ClearPending();
            }

            if (!observed)
            {
                return false;
            }

            if (pendingCount == 0)
            {
                firstPending = Sequence;
                server.QueueState(Record);
            }

            if (pendingKinds.Length == pendingCount)
            {
                Array.Resize(ref pendingKinds, Math.Max(8, pendingCount * 2));
            }

            pendingKinds[pendingCount++] = (byte)kind;
            return true;
        }

        protected void WriteIndex(int index) => pendingIndexes.Add((uint)index);

        protected void WriteKey(ReadOnlyMemory<byte> bytes) => pendingKeys.Add(bytes.Span);

        protected void WriteValue(ReadOnlyMemory<byte> bytes) => pendingValues.Add(bytes.Span);

        protected static MessageDecodeException Unappliable(string reason) => new MessageDecodeException(reason, null);

        protected abstract bool Carries(ChangeKind kind, out bool index, out bool key, out bool value);

        protected abstract void Apply(ChangeKind kind, uint index, ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, bool shared);

        protected abstract void WriteContent(ByteSegments keys, ByteSegments values);

        protected abstract void StageContent(List<ReadOnlyMemory<byte>> keys, List<ReadOnlyMemory<byte>> values);

        protected abstract void CommitContent(bool shared, bool reset);
    }

    internal sealed class ByteSegments
    {
        private readonly List<int> ends = new List<int>();
        private byte[] bytes = Array.Empty<byte>();
        private int length;

        public void Add(ReadOnlySpan<byte> segment)
        {
            if (bytes.Length < length + segment.Length)
            {
                Array.Resize(ref bytes, Math.Max(length + segment.Length, bytes.Length * 2));
            }

            segment.CopyTo(new Span<byte>(bytes, length, segment.Length));
            length += segment.Length;
            ends.Add(length);
        }

        public void CopyTo(List<ReadOnlyMemory<byte>> target)
        {
            target.Clear();
            int start = 0;
            foreach (int end in ends)
            {
                target.Add(new ReadOnlyMemory<byte>(bytes, start, end - start));
                start = end;
            }
        }

        public void Clear()
        {
            ends.Clear();
            length = 0;
        }
    }
}
