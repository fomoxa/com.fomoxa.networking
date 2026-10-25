using System;
using Fomoxa.Net;

namespace Fomoxa.Networking.Sessions
{
    public enum ServerState
    {
        Stopped,
        Starting,
        Started,
        Stopping,
    }

    public enum ConnectionState
    {
        Stopped,
        Starting,
        Started,
    }

    public enum StopReason
    {
        None,
        LocalStop,
        PeerClosed,
        TransportError,
        Timeout,
        HandshakeFailed,
        HandlerException,
        PrefabMismatch,
        ObjectMismatch,
        PhysicsBackendMismatch,
        InvalidFragment,
        ServerClosed,
    }

    public enum SessionCloseReason : byte
    {
        Unspecified = 0,
        HandlerException = 1,
        Kicked = 2,
    }

    public readonly struct ServerConnectionStateArgs
    {
        public ServerConnectionStateArgs(ServerState state)
        {
            State = state;
        }

        public ServerState State { get; }
    }

    public readonly struct ConnectionStateArgs
    {
        public ConnectionStateArgs(
            ulong peerId,
            ConnectionState state,
            StopReason reason,
            HandshakeFailure failure,
            int discardedMessages,
            bool willRetry = false,
            SessionCloseReason closeReason = SessionCloseReason.Unspecified)
        {
            PeerId = peerId;
            State = state;
            Reason = reason;
            Failure = failure;
            DiscardedMessages = discardedMessages;
            WillRetry = willRetry;
            CloseReason = closeReason;
        }

        public ulong PeerId { get; }

        public ConnectionState State { get; }

        public StopReason Reason { get; }

        public HandshakeFailure Failure { get; }

        public int DiscardedMessages { get; }

        public bool WillRetry { get; }

        public SessionCloseReason CloseReason { get; }
    }

    public readonly struct SendDroppedArgs
    {
        public SendDroppedArgs(ulong peerId, uint messageId, int payloadLength, SendStatus reason)
        {
            PeerId = peerId;
            MessageId = messageId;
            PayloadLength = payloadLength;
            Reason = reason;
        }

        public ulong PeerId { get; }

        public uint MessageId { get; }

        public int PayloadLength { get; }

        public SendStatus Reason { get; }
    }

    public readonly struct ReceiveDroppedArgs
    {
        public ReceiveDroppedArgs(ulong peerId, int frameLength)
        {
            PeerId = peerId;
            FrameLength = frameLength;
        }

        public ulong PeerId { get; }

        public int FrameLength { get; }
    }

    public readonly struct HandlerExceptionArgs
    {
        public HandlerExceptionArgs(ulong peerId, uint messageId, Exception exception)
        {
            PeerId = peerId;
            MessageId = messageId;
            Exception = exception;
        }

        public ulong PeerId { get; }

        public uint MessageId { get; }

        public Exception Exception { get; }
    }

    public sealed class SessionLimits
    {
        public int MessageCapacity { get; set; } = 1024;

        public int ByteCapacity { get; set; } = 1024 * 1024;

        public int ReliableWindow { get; set; } = 1024;

        public TimeSpan CloseTimeout { get; set; } = TimeSpan.FromSeconds(1);
    }
}