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
            IMessageCodec<SessionClose> closeCodec,
            IMessageCodec<PeerTransfer> transferCodec,
            MessageChannels channels)
        {
            Bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
            AckCodec = ackCodec ?? throw new ArgumentNullException(nameof(ackCodec));
            LeaveCodec = leaveCodec ?? throw new ArgumentNullException(nameof(leaveCodec));
            FragmentStartCodec = fragmentStartCodec ?? throw new ArgumentNullException(nameof(fragmentStartCodec));
            FragmentPartCodec = fragmentPartCodec ?? throw new ArgumentNullException(nameof(fragmentPartCodec));
            CloseCodec = closeCodec ?? throw new ArgumentNullException(nameof(closeCodec));
            TransferCodec = transferCodec ?? throw new ArgumentNullException(nameof(transferCodec));
            Channels = channels ?? throw new ArgumentNullException(nameof(channels));
            ObjectProtocol.EnsureReliable(channels, fragmentStartCodec.MessageId, nameof(FragmentStart));
            ObjectProtocol.EnsureReliable(channels, fragmentPartCodec.MessageId, nameof(FragmentPart));
            ObjectProtocol.EnsureReliable(channels, closeCodec.MessageId, nameof(SessionClose));
            ObjectProtocol.EnsureReliable(channels, transferCodec.MessageId, nameof(PeerTransfer));
        }

        public BundleFormat Bundle { get; }

        public IMessageCodec<ReliableAck> AckCodec { get; }

        public IMessageCodec<PeerLeave> LeaveCodec { get; }

        public IMessageCodec<FragmentStart> FragmentStartCodec { get; }

        public IMessageCodec<FragmentPart> FragmentPartCodec { get; }

        public IMessageCodec<SessionClose> CloseCodec { get; }

        public IMessageCodec<PeerTransfer> TransferCodec { get; }

        public MessageChannels Channels { get; }

        public bool IsFragment(uint messageId) =>
            messageId == FragmentStartCodec.MessageId || messageId == FragmentPartCodec.MessageId;
    }
}
