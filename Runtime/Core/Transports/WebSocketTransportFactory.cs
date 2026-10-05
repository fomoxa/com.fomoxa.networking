using System;
using System.Net;
using System.Net.Sockets;
using Fomoxa.Net;
using Fomoxa.Net.Transports;

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
            var listener = new WebSocketListener(new IPEndPoint(IPAddress.Any, port), Settings);
            boundPort = (ushort)listener.LocalEndPoint.Port;
            return listener;
        }

        public ITransportConnector CreateConnector(string address, ushort port)
        {
            Endpoint endpoint = Parse(address, port, Settings.Path);
            return new WebSocketConnector(new HostResolver(endpoint.Host), endpoint.Port, endpoint.Target, endpoint.HostHeader, Settings);
        }

        public static string UrlFor(string address, ushort port, string path)
        {
            if (address != null
                && (address.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) || address.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)))
            {
                if (!Uri.TryCreate(address, UriKind.Absolute, out Uri uri) || uri.Host.Length == 0 || uri.Fragment.Length > 0)
                {
                    throw new ArgumentException($"'{address}' is not a valid WebSocket URL", nameof(address));
                }

                return address;
            }

            Endpoint endpoint = Parse(address, port, path);
            return $"ws://{endpoint.HostHeader}{endpoint.Target}";
        }

        internal static Endpoint Parse(string address, ushort port, string path)
        {
            if (string.IsNullOrEmpty(address))
            {
                throw new ArgumentException("a WebSocket address is a host name, an IP address or a ws:// URL", nameof(address));
            }

            if (address.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException("the native WebSocket transport does not do TLS; connect with ws:// or put a TLS-terminating proxy in front of the server for browser clients");
            }

            if (!address.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
            {
                string literalHost = IPAddress.TryParse(address, out IPAddress literal) && literal.AddressFamily == AddressFamily.InterNetworkV6
                    ? $"[{address}]"
                    : address;
                return new Endpoint(address, port, path, $"{literalHost}:{port}");
            }

            if (!Uri.TryCreate(address, UriKind.Absolute, out Uri uri) || uri.Host.Length == 0 || uri.Fragment.Length > 0)
            {
                throw new ArgumentException($"'{address}' is not a valid ws:// URL", nameof(address));
            }

            return new Endpoint(uri.DnsSafeHost, (ushort)uri.Port, uri.PathAndQuery, uri.Authority);
        }

        internal readonly struct Endpoint
        {
            public Endpoint(string host, ushort port, string target, string hostHeader)
            {
                Host = host;
                Port = port;
                Target = target;
                HostHeader = hostHeader;
            }

            public string Host { get; }

            public ushort Port { get; }

            public string Target { get; }

            public string HostHeader { get; }
        }
    }
}
