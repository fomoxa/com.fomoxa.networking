using System;
using System.Collections.Generic;
using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Transports;

namespace Fomoxa.Networking.Sessions
{
    internal delegate bool PeerStartHandler(ulong peerId, out uint failedMessageId, out Exception exception);

    public sealed class ServerSession
    {
        private readonly Schema schema;
        private readonly SessionConfig config;
        private readonly SessionLimits limits;
        private readonly MessageDispatcher dispatcher;
        private readonly SessionProtocol protocol;
        private readonly SessionInbox inbox;
        private readonly Dictionary<ulong, Peer> peers = new Dictionary<ulong, Peer>();
        private readonly List<ulong> stoppingPeerIds = new List<ulong>();
        private readonly List<Peer> flushingPeers = new List<Peer>();
        private readonly List<QueuedMessage> heldMessages = new List<QueuedMessage>();

        private FomoxaServer server;
        private PeerStartHandler peerStart;
        private TimeSpan lastNow;

        public ServerSession(Schema schema, SessionConfig config, SessionLimits limits, MessageDispatcher dispatcher, SessionProtocol protocol)
        {
            this.schema = schema ?? throw new ArgumentNullException(nameof(schema));
            this.config = config ?? new SessionConfig();
            this.limits = limits ?? new SessionLimits();
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
            inbox = new SessionInbox(protocol, dispatcher, (peerId, length) => OnReceiveDropped?.Invoke(new ReceiveDroppedArgs(peerId, length)));
        }

        public event Action<ServerConnectionStateArgs> OnServerConnectionState;

        public event Action<ConnectionStateArgs> OnRemoteConnectionState;

        public event Action<SendDroppedArgs> OnSendDropped;

        public event Action<ReceiveDroppedArgs> OnReceiveDropped;

        public event Action<HandlerExceptionArgs> OnHandlerException;

        public ServerState State { get; private set; } = ServerState.Stopped;

        public int PeerCount => peers.Count;

        internal MessageDispatcher Dispatcher => dispatcher;

        public ConnectionState PeerState(ulong peerId) =>
            peers.TryGetValue(peerId, out Peer peer) ? peer.State : ConnectionState.Stopped;

        public void Start(IListenerTransport listener)
        {
            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            if (State != ServerState.Stopped)
            {
                throw new InvalidOperationException("the server session is already running");
            }

            ChangeState(ServerState.Starting);
            server = new FomoxaServer(listener, schema, config);
            ChangeState(ServerState.Started);
        }

        public SendResult Send(ulong peerId, uint messageId, ReadOnlySpan<byte> payload)
        {
            if (!peers.TryGetValue(peerId, out Peer peer))
            {
                return SendResult.NotConnected;
            }

            return SendResults.From(peer.Outbox.Enqueue(messageId, payload));
        }

        public SendResult SendToObject(ulong peerId, uint messageId, uint objectId, byte behaviourIndex, ReadOnlySpan<byte> body)
        {
            if (!peers.TryGetValue(peerId, out Peer peer))
            {
                return SendResult.NotConnected;
            }

            return SendResults.From(peer.Outbox.EnqueueToObject(messageId, objectId, behaviourIndex, body));
        }

        public int Broadcast(uint messageId, ReadOnlySpan<byte> payload)
        {
            int queued = 0;
            foreach (Peer peer in peers.Values)
            {
                if (peer.State == ConnectionState.Started && peer.Outbox.Enqueue(messageId, payload) == EnqueueResult.Queued)
                {
                    queued++;
                }
            }

            return queued;
        }

        public int BroadcastToObject(uint messageId, uint objectId, byte behaviourIndex, ReadOnlySpan<byte> body)
        {
            int queued = 0;
            foreach (Peer peer in peers.Values)
            {
                if (peer.State == ConnectionState.Started && peer.Outbox.EnqueueToObject(messageId, objectId, behaviourIndex, body) == EnqueueResult.Queued)
                {
                    queued++;
                }
            }

            return queued;
        }

        public int BroadcastToObject(IReadOnlyList<ulong> peerIds, uint messageId, uint objectId, byte behaviourIndex, ReadOnlySpan<byte> body)
        {
            int queued = 0;
            for (int index = 0; index < peerIds.Count; index++)
            {
                if (peers.TryGetValue(peerIds[index], out Peer peer)
                    && peer.State == ConnectionState.Started
                    && peer.Outbox.EnqueueToObject(messageId, objectId, behaviourIndex, body) == EnqueueResult.Queued)
                {
                    queued++;
                }
            }

            return queued;
        }

        public void Tick(TimeSpan now)
        {
            if (State != ServerState.Started)
            {
                return;
            }

            lastNow = now;
            IReadOnlyList<FomoxaEvent> events = server.Tick(now);
            for (int index = 0; index < events.Count && State == ServerState.Started; index++)
            {
                FomoxaEvent raised = events[index];
                if (raised.Kind == FomoxaEventKind.Connected)
                {
                    Connect(raised.PeerId);
                    continue;
                }

                if (!peers.TryGetValue(raised.PeerId, out Peer peer))
                {
                    continue;
                }

                switch (raised.Kind)
                {
                    case FomoxaEventKind.Ready:
                        if (StartPeer(peer))
                        {
                            RaisePeer(peer.Id, ConnectionState.Started, StopReason.None, default, 0);
                        }

                        break;
                    case FomoxaEventKind.Message:
                        Receive(peer, raised.MessageId, raised.Payload, now);
                        break;
                    case FomoxaEventKind.HandshakeFailed:
                        End(peer, StopReason.HandshakeFailed, raised.Failure);
                        break;
                    case FomoxaEventKind.Disconnected:
                        End(peer, FlushPolicy.ToStopReason(raised.Reason), default);
                        break;
                }
            }
        }

        public void Flush()
        {
            if (State != ServerState.Started)
            {
                return;
            }

            flushingPeers.Clear();
            flushingPeers.AddRange(peers.Values);
            foreach (Peer peer in flushingPeers)
            {
                if (State != ServerState.Started)
                {
                    return;
                }

                if (peers.ContainsKey(peer.Id))
                {
                    peer.Discarded += peer.Outbox.Flush(peer.Sender, lastNow, peer.Id, OnSendDropped);
                }
            }
        }

        public void Disconnect(ulong peerId)
        {
            if (!peers.TryGetValue(peerId, out Peer peer))
            {
                return;
            }

            Disconnect(peer, StopReason.LocalStop);
        }

        public void Stop()
        {
            if (State != ServerState.Started)
            {
                return;
            }

            ChangeState(ServerState.Stopping);
            stoppingPeerIds.Clear();
            stoppingPeerIds.AddRange(peers.Keys);
            foreach (ulong peerId in stoppingPeerIds)
            {
                Disconnect(peerId);
            }

            server.Dispose();
            server = null;
            ChangeState(ServerState.Stopped);
        }

        internal void SetPeerStartHandler(PeerStartHandler handler)
        {
            if (peerStart != null)
            {
                throw new InvalidOperationException("the server session already has a peer start handler");
            }

            peerStart = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        internal void EnqueueObjectMessage(ulong peerId, uint messageId, ReadOnlySpan<byte> payload)
        {
            if (peers.TryGetValue(peerId, out Peer peer))
            {
                peer.Outbox.EnqueueUnbounded(messageId, payload);
            }
        }

        internal void EnqueueObjectMessageToStarted(uint messageId, ReadOnlySpan<byte> payload)
        {
            foreach (Peer peer in peers.Values)
            {
                if (peer.State == ConnectionState.Started)
                {
                    peer.Outbox.EnqueueUnbounded(messageId, payload);
                }
            }
        }

        internal void CopyStartedPeerIds(List<ulong> peerIds)
        {
            foreach (Peer peer in peers.Values)
            {
                if (peer.State == ConnectionState.Started)
                {
                    peerIds.Add(peer.Id);
                }
            }
        }

        internal void EndPeerByHandler(ulong peerId, uint messageId, Exception exception)
        {
            if (peers.TryGetValue(peerId, out Peer peer))
            {
                EndByHandler(peer, messageId, exception);
            }
        }

        private bool StartPeer(Peer peer)
        {
            peer.State = ConnectionState.Started;
            if (peerStart == null)
            {
                return true;
            }

            OutgoingQueue queue = peer.Outbox.Queue;
            queue.MoveAllTo(heldMessages);
            bool started;
            uint failedMessageId;
            Exception exception;
            try
            {
                started = peerStart(peer.Id, out failedMessageId, out exception);
            }
            finally
            {
                queue.AppendAll(heldMessages);
                heldMessages.Clear();
            }

            if (started)
            {
                return true;
            }

            EndByHandler(peer, failedMessageId, exception);
            return false;
        }

        private void Receive(Peer peer, uint messageId, ReadOnlyMemory<byte> payload, TimeSpan now)
        {
            ReliableChannel reliable = peer.Outbox.Reliable;
            if (!inbox.TryRead(messageId, payload, reliable))
            {
                OnReceiveDropped?.Invoke(new ReceiveDroppedArgs(peer.Id, FomoxaWire.DataFrameHeaderSize + payload.Length));
                return;
            }

            for (int index = 0; index < inbox.Count && IsDelivering(peer); index++)
            {
                inbox.Deliver(index, peer.Id, reliable, now);
                while (IsDelivering(peer) && inbox.DeliverNextBuffered(peer.Id, reliable))
                {
                }
            }

            inbox.Release();
            if (inbox.TakeFailure(out uint failedMessageId, out Exception exception))
            {
                EndByHandler(peer, failedMessageId, exception);
            }
            else if (inbox.TakeLeave() && IsServing(peer))
            {
                Disconnect(peer, StopReason.PeerClosed);
            }
        }

        private bool IsDelivering(Peer peer) => IsServing(peer) && !inbox.HasFailed;

        private bool IsServing(Peer peer) =>
            State == ServerState.Started && peers.TryGetValue(peer.Id, out Peer current) && current == peer;

        private void EndByHandler(Peer peer, uint messageId, Exception exception)
        {
            OnHandlerException?.Invoke(new HandlerExceptionArgs(peer.Id, messageId, exception));
            if (IsServing(peer))
            {
                Disconnect(peer, StopReason.HandlerException);
            }
        }

        private void Disconnect(Peer peer, StopReason reason)
        {
            server.Disconnect(peer.Id);
            End(peer, reason, default);
        }

        private void Connect(ulong peerId)
        {
            FomoxaServer owner = server;
            var outbox = new SessionOutbox(
                new OutgoingQueue(limits.MessageCapacity, limits.ByteCapacity),
                protocol,
                new ReliableChannel(limits.ReliableWindow),
                BundleOf(owner.Listener));
            var peer = new Peer(peerId, outbox, (messageId, payload) => owner.Send(peerId, messageId, payload));
            peers.Add(peerId, peer);
            RaisePeer(peerId, ConnectionState.Starting, StopReason.None, default, 0);
        }

        private BundleFormat BundleOf(IListenerTransport listener) =>
            listener is CompositeListener composite
                && composite.TryTakeAcceptedBudget(out int budget)
                && budget != protocol.Bundle.FrameBudget
                    ? new BundleFormat(protocol.Bundle.Codec, budget)
                    : protocol.Bundle;

        public int FrameBudgetOf(ulong peerId) =>
            peers.TryGetValue(peerId, out Peer peer) ? peer.Outbox.FrameBudget : 0;

        private void End(Peer peer, StopReason reason, HandshakeFailure failure)
        {
            int total = peer.Discarded + peer.Outbox.Queue.Clear();
            peers.Remove(peer.Id);
            RaisePeer(peer.Id, ConnectionState.Stopped, reason, failure, total);
        }

        private void RaisePeer(ulong peerId, ConnectionState state, StopReason reason, HandshakeFailure failure, int discardedMessages)
        {
            OnRemoteConnectionState?.Invoke(new ConnectionStateArgs(peerId, state, reason, failure, discardedMessages));
        }

        private void ChangeState(ServerState state)
        {
            State = state;
            OnServerConnectionState?.Invoke(new ServerConnectionStateArgs(state));
        }

        private sealed class Peer
        {
            public Peer(ulong id, SessionOutbox outbox, MessageSender sender)
            {
                Id = id;
                Outbox = outbox;
                Sender = sender;
            }

            public ulong Id { get; }

            public SessionOutbox Outbox { get; }

            public MessageSender Sender { get; }

            public ConnectionState State { get; set; } = ConnectionState.Starting;

            public int Discarded { get; set; }
        }
    }
}