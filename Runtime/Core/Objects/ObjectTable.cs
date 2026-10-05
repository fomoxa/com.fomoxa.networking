using System;
using System.Collections.Generic;

namespace Fomoxa.Networking.Objects
{
    public readonly struct ObjectRow
    {
        public ObjectRow(uint objectId, uint prefabId, ulong ownerId)
            : this(objectId, prefabId, 0, ownerId)
        {
        }

        public ObjectRow(uint objectId, uint prefabId, ulong sceneObjectId, ulong ownerId)
            : this(objectId, prefabId, sceneObjectId, ownerId, 0)
        {
        }

        public ObjectRow(uint objectId, uint prefabId, ulong sceneObjectId, ulong ownerId, uint sceneId)
        {
            ObjectId = objectId;
            PrefabId = prefabId;
            SceneObjectId = sceneObjectId;
            OwnerId = ownerId;
            SceneId = sceneId;
        }

        public uint ObjectId { get; }

        public uint PrefabId { get; }

        public ulong SceneObjectId { get; }

        public ulong OwnerId { get; }

        public uint SceneId { get; }
    }

    public sealed class ObjectTable
    {
        private readonly Dictionary<uint, LinkedListNode<ObjectRow>> index = new Dictionary<uint, LinkedListNode<ObjectRow>>();
        private readonly LinkedList<ObjectRow> spawnOrder = new LinkedList<ObjectRow>();

        public int Count => index.Count;

        public bool Contains(uint objectId) => index.ContainsKey(objectId);

        public bool TryGet(uint objectId, out ObjectRow row)
        {
            if (index.TryGetValue(objectId, out LinkedListNode<ObjectRow> node))
            {
                row = node.Value;
                return true;
            }

            row = default;
            return false;
        }

        public bool TryAdd(ObjectRow row)
        {
            var node = new LinkedListNode<ObjectRow>(row);
            if (!index.TryAdd(row.ObjectId, node))
            {
                return false;
            }

            spawnOrder.AddLast(node);
            return true;
        }

        public void Add(ObjectRow row)
        {
            if (!TryAdd(row))
            {
                throw new ArgumentException($"object id {row.ObjectId} is already in the table", nameof(row));
            }
        }

        public bool Remove(uint objectId)
        {
            if (!index.Remove(objectId, out LinkedListNode<ObjectRow> node))
            {
                return false;
            }

            spawnOrder.Remove(node);
            return true;
        }

        public bool SetOwner(uint objectId, ulong ownerId, out ulong previousOwnerId)
        {
            if (!index.TryGetValue(objectId, out LinkedListNode<ObjectRow> node))
            {
                previousOwnerId = 0;
                return false;
            }

            ObjectRow row = node.Value;
            previousOwnerId = row.OwnerId;
            node.Value = new ObjectRow(row.ObjectId, row.PrefabId, row.SceneObjectId, ownerId, row.SceneId);
            return true;
        }

        public void Clear()
        {
            index.Clear();
            spawnOrder.Clear();
        }

        public void CopyIdsTo(List<uint> objectIds)
        {
            foreach (ObjectRow row in spawnOrder)
            {
                objectIds.Add(row.ObjectId);
            }
        }

        public LinkedList<ObjectRow>.Enumerator GetEnumerator() => spawnOrder.GetEnumerator();
    }
}
