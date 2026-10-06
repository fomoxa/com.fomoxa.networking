using System.Collections;
using System.Collections.Generic;
using Fomoxa.Networking;

namespace Fomoxa.Unity
{
    internal sealed class RepresentationView : IReadOnlyDictionary<uint, NetworkObject>
    {
        private readonly IReadOnlyDictionary<uint, EntityRecord> records;

        public RepresentationView(IReadOnlyDictionary<uint, EntityRecord> records)
        {
            this.records = records;
        }

        public int Count => records.Count;

        public IEnumerable<uint> Keys => records.Keys;

        public IEnumerable<NetworkObject> Values
        {
            get
            {
                foreach (EntityRecord record in records.Values)
                {
                    yield return (NetworkObject)record.Representation;
                }
            }
        }

        public NetworkObject this[uint key] => (NetworkObject)records[key].Representation;

        public bool ContainsKey(uint key) => records.ContainsKey(key);

        public bool TryGetValue(uint key, out NetworkObject value)
        {
            if (records.TryGetValue(key, out EntityRecord record))
            {
                value = (NetworkObject)record.Representation;
                return true;
            }

            value = null;
            return false;
        }

        public IEnumerator<KeyValuePair<uint, NetworkObject>> GetEnumerator()
        {
            foreach (KeyValuePair<uint, EntityRecord> pair in records)
            {
                yield return new KeyValuePair<uint, NetworkObject>(pair.Key, (NetworkObject)pair.Value.Representation);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
