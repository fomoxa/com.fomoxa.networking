using System;
using System.Collections.Generic;
using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Transports;

namespace Fomoxa.Networking.Sessions
{
    public sealed class ClientSession
    {
        private const int LeaveAttempts = 3;

        private readonly Schema schema;
        private readonly SessionConfig config;
        private readonly SessionLimits limits;
        private readonly MessageDispatcher dispatcher;
        private readonly SessionProtocol protocol;
        private readonly SessionInbox inbox;
        private readonly PeerLeave leave = new PeerLeave();

        private ITransportConnector connector;
        private FomoxaConnection connection;
        private SessionOutbox outbox;
        private MessageSender sender;
        private TimeSpan lastNow;
        private int discarded;
        private Action stopped;

        public ClientSession(Schema schema, SessionConfig config, SessionLimits limits, MessageDispatcher dispatcher, SessionProtocol protocol)
        {
            this.schema = schema ?? throw new ArgumentNullException(nameof(schema));
            this.config = config ?? new SessionConfig();
            this.limits = limits ?? new SessionLimits();
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
            inbox = new SessionInbox(protocol, dispatcher, (peerId, length) => OnReceiveDropped?.Invoke(new ReceiveDroppedArgs(peerId, length)));
        }

        public event Action<ConnectionStateArgs> OnClientConnectionState;

        public event Action<SendDroppedArgs> OnSendDropped;

        public event Action<ReceiveDroppedArgs> OnReceiveDropped;

        public event Action<HandlerExceptionArgs> OnHandlerException;

        public ConnectionState State { get; private set; } = ConnectionState.Stopped;

        internal MessageDispatcher Dispatcher => dispatcher;

        public void Start(ITransport transport, TimeSpan now)
        {
            if (transport == null)
            {
                throw new ArgumentNullException(nameof(transport));
            }

            EnsureStopped();
            Open();
            Attach(transport, now);
            ChangeState(ConnectionState.Starting, StopReason.None, default, 0);
        }

        public void Start(ITransportConnector pendingConnector)
        {
            if (pendingConnector == null)
            {
                throw new ArgumentNullException(nameof(pendingConnector));
            }

            EnsureStopped();
            Open();
            connector = pendingConnector;
            ChangeState(ConnectionState.Starting, StopReason.None, default, 0);
        }

        public SendResult Send(uint messageId, ReadOnlySpan<byte> payload)
        {
            if (State == ConnectionState.Stopped)
            {
                return SendResult.NotConnected;
            }

            return SendResults.From(outbox.Enqueue(messageId, payload));
        }

        public SendResult SendToObject(uint messageId, uint objectId, byte behaviourIndex, ReadOnlySpan<byte> body)
        {
            if (State == ConnectionState.Stopped)
            {
                return SendResult.NotConnected;
            }

            return SendResults.From(outbox.EnqueueToObject(messageId, objectId, behaviourIndex, body));
        }

        internal void EnqueueObjectMessage(uint messageId, ReadOnlySpan<byte> payload)
        {
            if (State != ConnectionState.Stopped)
            {
                outbox.EnqueueUnbounded(messageId, payload);
            }
        }

        public void Tick(TimeSpan now)
        {
            if (State == ConnectionState.Stopped)
            {
                return;
            }

            lastNow = now;
            if (connector != null)
            {
                ConnectStatus status = connector.Poll(out ITransport transport);
                if (status == ConnectStatus.Pending)
                {
                    return;
                }

                connector = null;
                if (status == ConnectStatus.Failed)
                {
                    End(StopReason.TransportError, default);
                    return;
                }

                Attach(transport, now);
            }

            IReadOnlyList<FomoxaEvent> events = connection.Tick(now);
            for (int index = 0; index < events.Count && State != ConnectionState.Stopped; index++)
            {
                FomoxaEvent raised = events[index];
                switch (raised.Kind)
                {
                    case FomoxaEventKind.Ready:
                        ChangeState(ConnectionState.Started, StopReason.None, default, 0);
                        break;
                    case FomoxaEventKind.Message:
                        Receive(raised.MessageId, raised.Payload, now);
                        break;
                    case FomoxaEventKind.HandshakeFailed:
                        End(StopReason.HandshakeFailed, raised.Failure);
                        break;
                    case FomoxaEventKind.Disconnected:
                        End(FlushPolicy.ToStopReason(raised.Reason), default);
                        break;
                }
            }
        }

        public void Flush()
        {
            if (connection == null)
            {
                return;
            }

            discarded += outbox.Flush(sender, lastNow, 0, OnSendDropped);
        }

        public void Stop()
        {
            if (State == ConnectionState.Stopped)
            {
                return;
            }

            try
            {
                if (State == ConnectionState.Started)
                {
                    SendLeave();
                }
            }
            finally
            {
                connection?.Close();
                End(StopReason.LocalStop, default);
            }
        }

        internal void SetStoppedHandler(Action handler)
        {
            if (stopped != null)
            {
                throw new InvalidOperationException("the client session already has a stopped handler");
            }

            stopped = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        internal void Abort(StopReason reason)
        {
            if (State == ConnectionState.Stopped)
            {
                return;
            }

            connection?.Close();
            End(reason, default);
        }

        private void Receive(uint messageId, ReadOnlyMemory<byte> payload, TimeSpan now)
        {
            ReliableChannel reliable = outbox.Reliable;
            if (!inbox.TryRead(messageId, payload, reliable))
            {
                OnReceiveDropped?.Invoke(new ReceiveDroppedArgs(0, FomoxaWire.DataFrameHeaderSize + payload.Length));
                return;
            }

            for (int index = 0; index < inbox.Count && IsDelivering(); index++)
            {
                inbox.Deliver(index, 0, reliable, now);
                while (IsDelivering() && inbox.DeliverNextBuffered(0, reliable))
                {
                }
            }

            inbox.Release();
            if (inbox.TakeFailure(out uint failedMessageId, out Exception exception))
            {
                EndByHandler(failedMessageId, exception);
            }
        }

        private bool IsDelivering() => State != ConnectionState.Stopped && !inbox.HasFailed;

        private void SendLeave()
        {
            discarded += outbox.Queue.Clear();
            ReadOnlySpan<byte> payload = protocol.LeaveCodec.Encode(leave).Span;
            for (int attempt = 0; attempt < LeaveAttempts; attempt++)
            {
                outbox.EnqueueUnbounded(protocol.LeaveCodec.MessageId, payload);
                Flush();
            }
        }

        private void EndByHandler(uint messageId, Exception exception)
        {
            OnHandlerException?.Invoke(new HandlerExceptionArgs(0, messageId, exception));
            if (State == ConnectionState.Stopped)
            {
                return;
            }

            connection.Close();
            End(StopReason.HandlerException, default);
        }

        private void EnsureStopped()
        {
            if (State != ConnectionState.Stopped)
            {
                throw new InvalidOperationException("the client session is already running");
            }
        }

        private void Open()
        {
            outbox = new SessionOutbox(
                new OutgoingQueue(limits.MessageCapacity, limits.ByteCapacity),
                protocol,
                new ReliableChannel(limits.ReliableWindow));
            discarded = 0;
        }

        private void Attach(ITransport transport, TimeSpan now)
        {
            connection = FomoxaConnection.Connect(transport, schema, config, now);
            sender = connection.Send;
            lastNow = now;
        }

        private void End(StopReason reason, HandshakeFailure failure)
        {
            int total = discarded + outbox.Queue.Clear();
            connector?.Dispose();
            connection?.Dispose();
            connector = null;
            connection = null;
            outbox = null;
            sender = null;
            discarded = 0;
            ChangeState(ConnectionState.Stopped, reason, failure, total);
            stopped?.Invoke();
        }

        private void ChangeState(ConnectionState state, StopReason reason, HandshakeFailure failure, int discardedMessages)
        {
            State = state;
            OnClientConnectionState?.Invoke(new ConnectionStateArgs(0, state, reason, failure, discardedMessages));
        }
    }
}
