using System.Net;
using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Net.WebSockets;

namespace Fomoxa.Networking.Transports
{
    public sealed class WebSocketTransportFactory : ITransportFactory
    {
        public WebSocketTransportFactory(WebSocketSettings settings = null)
        {
            Settings = settings ?? new WebSocketSettings();
        }

        public WebSocketSettings Settings { get; }

        public int FrameBudget => FomoxaWire.MaxDataFrameSize;

        public IListenerTransport CreateListener(ushort port, out ushort boundPort)
        {
            var listener = new WebSocketListener(new IPEndPoint(IPAddress.Any, port), Settings.ForPackage());
            boundPort = (ushort)listener.LocalEndPoint.Port;
            return listener;
        }

        public ITransportConnector CreateConnector(string address, ushort port)
        {
            WebSocketAddress endpoint = WebSocketAddress.Parse(address, port, Settings.Path);
            return endpoint.Secure
                ? new WebSocketConnector(new HostResolver(endpoint.Host), endpoint.Port, endpoint.Target, endpoint.HostHeader, Settings, endpoint.Host, Settings.Tls)
                : new WebSocketConnector(new HostResolver(endpoint.Host), endpoint.Port, endpoint.Target, endpoint.HostHeader, Settings);
        }

        public static string UrlFor(string address, ushort port, string path) => WebSocketAddress.UrlFor(address, port, path);
    }
}
