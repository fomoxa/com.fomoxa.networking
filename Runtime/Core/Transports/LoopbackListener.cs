using System;
using System.Collections.Generic;
using Fomoxa.Net.Transports;

namespace Fomoxa.Networking.Transports
{
    public sealed class LoopbackListener : IListenerTransport
    {
        private readonly Queue<LoopbackTransport> pendingServerEnds = new Queue<LoopbackTransport>();
        private readonly int maxQueuedPackets;

        public LoopbackListener(int maxQueuedPackets)
        {
            if (maxQueuedPackets < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxQueuedPackets));
            }

            this.maxQueuedPackets = maxQueuedPackets;
        }

        public LoopbackTransport Connect()
        {
            LoopbackTransport.CreatePair(maxQueuedPackets, out LoopbackTransport client, out LoopbackTransport server);
            pendingServerEnds.Enqueue(server);
            return client;
        }

        public AcceptOutcome Accept()
        {
            if (pendingServerEnds.Count == 0)
            {
                return AcceptOutcome.Pending;
            }

            return AcceptOutcome.Accepted(pendingServerEnds.Dequeue());
        }

        public void Dispose()
        {
            while (pendingServerEnds.Count > 0)
            {
                pendingServerEnds.Dequeue().Dispose();
            }
        }
    }
}
