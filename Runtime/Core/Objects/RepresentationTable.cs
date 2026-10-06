using System.Collections;
using System.Collections.Generic;

namespace Fomoxa.Networking.Objects
{
    internal sealed class RepresentationTable : IReadOnlyDictionary<uint, INetworkEntity>
    {
        private readonly Dictionary<uint, EntityRecord> records;

        public RepresentationTable(Dictionary<uint, EntityRecord> records)
        {
            this.records = records;
        }

        public int Count => records.Count;

        public IEnumerable<uint> Keys => records.Keys;

        public IEnumerable<INetworkEntity> Values
        {
            get
            {
                foreach (EntityRecord record in records.Values)
                {
                    yield return record.Representation;
                }
            }
        }

        public INetworkEntity this[uint key] => records[key].Representation;

        public bool ContainsKey(uint key) => records.ContainsKey(key);

        public bool TryGetValue(uint key, out INetworkEntity value)
        {
            if (records.TryGetValue(key, out EntityRecord record))
            {
                value = record.Representation;
                return true;
            }

            value = null;
            return false;
        }

        public IEnumerator<KeyValuePair<uint, INetworkEntity>> GetEnumerator()
        {
            foreach (KeyValuePair<uint, EntityRecord> pair in records)
            {
                yield return new KeyValuePair<uint, INetworkEntity>(pair.Key, pair.Value.Representation);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
