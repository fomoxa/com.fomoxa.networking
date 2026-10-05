using Fomoxa.Networking.Transports;

namespace Fomoxa.Unity
{
    public sealed class UdpNetworkTransport : NetworkTransport
    {
        public override ITransportFactory CreateFactory() =>
            RunsInBrowser ? throw NotInBrowser(nameof(UdpNetworkTransport)) : new UdpTransportFactory();
    }
}
