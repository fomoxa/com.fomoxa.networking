using System;

namespace Fomoxa.Networking.Messaging
{
    public sealed class SessionProtocol
    {
        public SessionProtocol(BundleFormat bundle, IMessageCodec<ReliableAck> ackCodec, IMessageCodec<PeerLeave> leaveCodec, MessageChannels channels)
        {
            Bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
            AckCodec = ackCodec ?? throw new ArgumentNullException(nameof(ackCodec));
            LeaveCodec = leaveCodec ?? throw new ArgumentNullException(nameof(leaveCodec));
            Channels = channels ?? throw new ArgumentNullException(nameof(channels));
        }

        public BundleFormat Bundle { get; }

        public IMessageCodec<ReliableAck> AckCodec { get; }

        public IMessageCodec<PeerLeave> LeaveCodec { get; }

        public MessageChannels Channels { get; }
    }
}
