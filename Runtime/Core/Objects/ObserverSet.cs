using System.Collections;
using System.Collections.Generic;

namespace Fomoxa.Networking.Objects
{
    internal sealed class ObserverSet : IReadOnlyList<ulong>
    {
        private readonly List<ulong> order;
        private readonly HashSet<ulong> members;

        public ObserverSet(int capacity)
        {
            order = new List<ulong>(capacity);
            members = new HashSet<ulong>();
        }

        public int Count => order.Count;

        public ulong this[int index] => order[index];

        public bool Contains(ulong peerId) => members.Contains(peerId);

        public void Add(ulong peerId)
        {
            if (members.Add(peerId))
            {
                order.Add(peerId);
            }
        }

        public bool Remove(ulong peerId)
        {
            if (!members.Remove(peerId))
            {
                return false;
            }

            order.Remove(peerId);
            return true;
        }

        public List<ulong>.Enumerator GetEnumerator() => order.GetEnumerator();

        IEnumerator<ulong> IEnumerable<ulong>.GetEnumerator() => order.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => order.GetEnumerator();
    }
}
