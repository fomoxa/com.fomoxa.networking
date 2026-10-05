using System.Collections.Generic;
using UnityEngine;

namespace Fomoxa.Unity
{
    public sealed class NetworkPrefabList : ScriptableObject
    {
        [SerializeField] private List<NetworkObject> prefabs = new List<NetworkObject>();

        public IReadOnlyList<NetworkObject> Prefabs => prefabs;

        internal void Set(IEnumerable<NetworkObject> values)
        {
            prefabs.Clear();
            prefabs.AddRange(values);
        }
    }
}
