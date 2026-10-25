using Fomoxa.Net;
using Fomoxa.Networking;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public static class TestBundles
    {
        public static BundleFormat Format(int frameBudget = FomoxaWire.MaxDataFrameSize) =>
            new BundleFormat(MessageBundleNetAdapter.Instance, frameBudget);

        public static SessionProtocol Protocol(MessageChannels channels = null, int frameBudget = FomoxaWire.MaxDataFrameSize)
        {
            channels ??= new MessageChannels();
            channels.Set(FragmentStartId, Channel.ReliableOrdered);
            channels.Set(FragmentPartId, Channel.ReliableOrdered);
            channels.Set(CloseId, Channel.ReliableOrdered);
            channels.Set(TransferId, Channel.ReliableOrdered);
            channels.Set(LocalPeerId, Channel.ReliableOrdered);
            return new SessionProtocol(
                Format(frameBudget),
                ReliableAckNetAdapter.Instance,
                PeerLeaveNetAdapter.Instance,
                FragmentStartNetAdapter.Instance,
                FragmentPartNetAdapter.Instance,
                SessionCloseNetAdapter.Instance,
                PeerTransferNetAdapter.Instance,
                channels);
        }

        public static uint MessageId => MessageBundleNetAdapter.Instance.MessageId;

        public static uint AckId => ReliableAckNetAdapter.Instance.MessageId;

        public static uint LeaveId => PeerLeaveNetAdapter.Instance.MessageId;

        public static uint FragmentStartId => FragmentStartNetAdapter.Instance.MessageId;

        public static uint FragmentPartId => FragmentPartNetAdapter.Instance.MessageId;

        public static uint CloseId => SessionCloseNetAdapter.Instance.MessageId;

        public static uint TransferId => PeerTransferNetAdapter.Instance.MessageId;

        public static uint LocalPeerId => LocalPeerNetAdapter.Instance.MessageId;
    }
}
