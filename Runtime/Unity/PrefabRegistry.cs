using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fomoxa.Unity
{
    public sealed class PrefabRegistry
    {
        private readonly Dictionary<uint, PrefabEntry> entries = new Dictionary<uint, PrefabEntry>();

        public int Count => entries.Count;

        public bool Contains(uint prefabId) => entries.ContainsKey(prefabId);

        public void Register(NetworkObject prefab)
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            uint prefabId = prefab.PrefabId;
            if (prefabId == 0)
            {
                throw new ArgumentException($"{prefab.name} has no prefab id; the Fomoxa Editor step assigns one when the prefab is saved", nameof(prefab));
            }

            if (entries.TryGetValue(prefabId, out PrefabEntry existing))
            {
                if (existing.Prefab == prefab)
                {
                    return;
                }

                throw new ArgumentException($"prefab id 0x{prefabId:X8} of {prefab.name} is already registered to {existing.Name}", nameof(prefab));
            }

            if (!PrefabHash.TryDescribe(prefab, out uint fingerprint, out string error))
            {
                throw new ArgumentException($"{prefab.name}: {error}", nameof(prefab));
            }

            entries.Add(prefabId, new PrefabEntry(prefabId, fingerprint, prefab, null, null));
        }

        public void Register(uint prefabId, uint fingerprint, Func<Vector3, Quaternion, NetworkObject> create, Action<NetworkObject> release = null)
        {
            if (prefabId == 0)
            {
                throw new ArgumentException("prefab id 0 means no prefab", nameof(prefabId));
            }

            if (create == null)
            {
                throw new ArgumentNullException(nameof(create));
            }

            if (entries.TryGetValue(prefabId, out PrefabEntry existing))
            {
                if (existing.Fingerprint == fingerprint && existing.Create == create && existing.Release == release)
                {
                    return;
                }

                throw new ArgumentException($"prefab id 0x{prefabId:X8} is already registered to {existing.Name}", nameof(prefabId));
            }

            entries.Add(prefabId, new PrefabEntry(prefabId, fingerprint, null, create, release));
        }

        internal bool TryGet(uint prefabId, out PrefabEntry entry) => entries.TryGetValue(prefabId, out entry);

        internal uint FingerprintOf(uint prefabId) =>
            entries.TryGetValue(prefabId, out PrefabEntry entry)
                ? entry.Fingerprint
                : throw new InvalidOperationException($"prefab id 0x{prefabId:X8} is not registered");
    }

    internal sealed class PrefabEntry
    {
        public PrefabEntry(uint prefabId, uint fingerprint, NetworkObject prefab, Func<Vector3, Quaternion, NetworkObject> create, Action<NetworkObject> release)
        {
            PrefabId = prefabId;
            Fingerprint = fingerprint;
            Prefab = prefab;
            Create = create;
            Release = release;
        }

        public uint PrefabId { get; }

        public uint Fingerprint { get; }

        public NetworkObject Prefab { get; }

        public Func<Vector3, Quaternion, NetworkObject> Create { get; }

        public Action<NetworkObject> Release { get; }

        public string Name => Create == null ? Prefab.name : "a factory";
    }
}
