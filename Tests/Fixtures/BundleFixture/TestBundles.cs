using Fomoxa.Net;
using Fomoxa.Networking.Messaging;

namespace BundleFixture
{
    public static class TestBundles
    {
        public static BundleFormat Format(int frameBudget = FomoxaWire.MaxDataFrameSize) =>
            new BundleFormat(MessageBundleNetAdapter.Instance, frameBudget);

        public static SessionProtocol Protocol(MessageChannels channels = null, int frameBudget = FomoxaWire.MaxDataFrameSize) =>
            new SessionProtocol(Format(frameBudget), ReliableAckNetAdapter.Instance, PeerLeaveNetAdapter.Instance, channels ?? new MessageChannels());

        public static uint MessageId => MessageBundleNetAdapter.Instance.MessageId;

        public static uint AckId => ReliableAckNetAdapter.Instance.MessageId;

        public static uint LeaveId => PeerLeaveNetAdapter.Instance.MessageId;
    }
}
