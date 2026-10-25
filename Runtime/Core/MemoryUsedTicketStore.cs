using System;
using System.Collections.Generic;

namespace Fomoxa.Networking
{
    public sealed class MemoryUsedTicketStore : IUsedTicketStore
    {
        private readonly Dictionary<Guid, DateTime> used = new Dictionary<Guid, DateTime>();
        private readonly Queue<KeyValuePair<DateTime, Guid>> expiries = new Queue<KeyValuePair<DateTime, Guid>>();
        private readonly object gate = new object();

        public int Count
        {
            get
            {
                lock (gate)
                {
                    return used.Count;
                }
            }
        }

        public bool TryUse(ReadOnlySpan<byte> nonce, DateTime expiresUtc, DateTime nowUtc)
        {
            if (nonce.Length != TransferTickets.NonceLength)
            {
                throw new ArgumentException($"a ticket nonce is {TransferTickets.NonceLength} bytes", nameof(nonce));
            }

            var key = new Guid(nonce);
            lock (gate)
            {
                Forget(nowUtc);
                if (used.ContainsKey(key))
                {
                    return false;
                }

                used.Add(key, expiresUtc);
                expiries.Enqueue(new KeyValuePair<DateTime, Guid>(expiresUtc, key));
                return true;
            }
        }

        private void Forget(DateTime nowUtc)
        {
            while (expiries.Count > 0 && expiries.Peek().Key <= nowUtc)
            {
                used.Remove(expiries.Dequeue().Value);
            }
        }
    }
}
