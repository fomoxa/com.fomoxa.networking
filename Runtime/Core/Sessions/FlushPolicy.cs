using Fomoxa.Net;

namespace Fomoxa.Networking.Sessions
{
    public static class FlushPolicy
    {
        public static StopReason ToStopReason(DisconnectReason reason)
        {
            switch (reason)
            {
                case DisconnectReason.PeerClosed:
                    return StopReason.PeerClosed;
                case DisconnectReason.Timeout:
                    return StopReason.Timeout;
                default:
                    return StopReason.TransportError;
            }
        }
    }
}
