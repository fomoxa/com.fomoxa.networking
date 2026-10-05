using Fomoxa.Networking.Transports;

namespace Fomoxa.Unity
{
    public sealed class TcpNetworkTransport : NetworkTransport
    {
        public override ITransportFactory CreateFactory() =>
            RunsInBrowser ? throw NotInBrowser(nameof(TcpNetworkTransport)) : new TcpTransportFactory();
    }
}
