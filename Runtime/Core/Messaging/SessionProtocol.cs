using System;
using Fomoxa.Networking.Objects;

namespace Fomoxa.Networking.Messaging
{
    public sealed class SessionProtocol
    {
        public SessionProtocol(
            BundleFormat bundle,
            IMessageCodec<ReliableAck> ackCodec,
            IMessageCodec<PeerLeave> leaveCodec,
            IMessageCodec<FragmentStart> fragmentStartCodec,
            IMessageCodec<FragmentPart> fragmentPartCodec,
            MessageChannels channels)
        {
            Bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
            AckCodec = ackCodec ?? throw new ArgumentNullException(nameof(ackCodec));
            LeaveCodec = leaveCodec ?? throw new ArgumentNullException(nameof(leaveCodec));
            FragmentStartCodec = fragmentStartCodec ?? throw new ArgumentNullException(nameof(fragmentStartCodec));
            FragmentPartCodec = fragmentPartCodec ?? throw new ArgumentNullException(nameof(fragmentPartCodec));
            Channels = channels ?? throw new ArgumentNullException(nameof(channels));
            ObjectProtocol.EnsureReliable(channels, fragmentStartCodec.MessageId, nameof(FragmentStart));
            ObjectProtocol.EnsureReliable(channels, fragmentPartCodec.MessageId, nameof(FragmentPart));
        }

        public BundleFormat Bundle { get; }

        public IMessageCodec<ReliableAck> AckCodec { get; }

        public IMessageCodec<PeerLeave> LeaveCodec { get; }

        public IMessageCodec<FragmentStart> FragmentStartCodec { get; }

        public IMessageCodec<FragmentPart> FragmentPartCodec { get; }

        public MessageChannels Channels { get; }

        public bool IsFragment(uint messageId) =>
            messageId == FragmentStartCodec.MessageId || messageId == FragmentPartCodec.MessageId;
    }
}
