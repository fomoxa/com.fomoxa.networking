using System;
using System.Collections.Generic;

namespace Fomoxa.Networking.Standalone
{
    public sealed class StandalonePrefabs
    {
        private readonly Dictionary<uint, Entry> entries = new Dictionary<uint, Entry>();

        public void Register(uint prefabId, uint fingerprint, Func<StandaloneEntity> create)
        {
            if (prefabId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(prefabId), "prefab id 0 means the object has no prefab");
            }

            if (create == null)
            {
                throw new ArgumentNullException(nameof(create));
            }

            if (entries.TryGetValue(prefabId, out Entry existing))
            {
                if (existing.Fingerprint == fingerprint && existing.Create == create)
                {
                    return;
                }

                throw new ArgumentException($"prefab id 0x{prefabId:X8} is already registered with another fingerprint or factory", nameof(prefabId));
            }

            entries.Add(prefabId, new Entry(fingerprint, create));
        }

        internal bool TryGet(uint prefabId, out uint fingerprint, out Func<StandaloneEntity> create)
        {
            if (entries.TryGetValue(prefabId, out Entry entry))
            {
                fingerprint = entry.Fingerprint;
                create = entry.Create;
                return true;
            }

            fingerprint = 0;
            create = null;
            return false;
        }

        private readonly struct Entry
        {
            public Entry(uint fingerprint, Func<StandaloneEntity> create)
            {
                Fingerprint = fingerprint;
                Create = create;
            }

            public uint Fingerprint { get; }

            public Func<StandaloneEntity> Create { get; }
        }
    }
}
