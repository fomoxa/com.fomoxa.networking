using System;
using System.Collections.Generic;
using Fomoxa.Net.Transports;

namespace Fomoxa.Networking.Transports
{
    public sealed class LoopbackTransport : ITransport
    {
        private readonly Queue<byte[]> inbound = new Queue<byte[]>();
        private readonly int maxQueuedPackets;
        private LoopbackTransport remote;
        private bool closedLocally;
        private bool closedByRemote;
        private bool disposed;

        private LoopbackTransport(int maxQueuedPackets)
        {
            this.maxQueuedPackets = maxQueuedPackets;
        }

        public static void CreatePair(int maxQueuedPackets, out LoopbackTransport client, out LoopbackTransport server)
        {
            if (maxQueuedPackets < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxQueuedPackets));
            }

            client = new LoopbackTransport(maxQueuedPackets);
            server = new LoopbackTransport(maxQueuedPackets);
            client.remote = server;
            server.remote = client;
        }

        public TransportKind Kind => TransportKind.Message;

        public int QueuedPacketCount => inbound.Count;

        public SendOutcome Send(ReadOnlySpan<byte> bytes)
        {
            if (disposed || closedLocally || closedByRemote)
            {
                return SendOutcome.Closed;
            }

            if (remote.disposed)
            {
                return SendOutcome.Error;
            }

            remote.Enqueue(bytes.ToArray());
            return SendOutcome.Ok;
        }

        public ReceiveOutcome Receive(Span<byte> buffer)
        {
            if (disposed)
            {
                return ReceiveOutcome.Closed;
            }

            if (inbound.Count > 0)
            {
                byte[] packet = inbound.Peek();
                if (packet.Length > buffer.Length)
                {
                    return ReceiveOutcome.NeedCapacity(packet.Length);
                }

                inbound.Dequeue();
                packet.CopyTo(buffer);
                return ReceiveOutcome.Ok(packet.Length);
            }

            if (closedByRemote)
            {
                return ReceiveOutcome.Closed;
            }

            if (remote.disposed)
            {
                return ReceiveOutcome.Error;
            }

            return ReceiveOutcome.WouldBlock;
        }

        public void CloseGracefully()
        {
            if (disposed || closedLocally)
            {
                return;
            }

            closedLocally = true;
            remote.closedByRemote = true;
        }

        public void Dispose()
        {
            disposed = true;
            inbound.Clear();
        }

        private void Enqueue(byte[] packet)
        {
            if (disposed)
            {
                return;
            }

            if (inbound.Count == maxQueuedPackets)
            {
                inbound.Dequeue();
            }

            inbound.Enqueue(packet);
        }
    }
}
