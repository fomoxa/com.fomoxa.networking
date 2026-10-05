namespace Fomoxa.Unity.Editor
{
    public readonly struct FomoxaPrefab
    {
        public FomoxaPrefab(string path, NetworkObject prefab, uint prefabId, uint fingerprint)
        {
            Path = path;
            Prefab = prefab;
            PrefabId = prefabId;
            Fingerprint = fingerprint;
        }

        public string Path { get; }

        public NetworkObject Prefab { get; }

        public uint PrefabId { get; }

        public uint Fingerprint { get; }
    }
}
