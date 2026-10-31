using System;
using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Networking;
using Fomoxa.Networking.Transports;
using UnityEngine;

namespace Fomoxa.Unity
{
    public sealed class EncryptedWebSocketNetworkTransport : NetworkTransport
    {
        [SerializeField] private string path = "/";
        [SerializeField] private int maxHandshakeBytes = 8 * 1024;
        [SerializeField] private float handshakeTimeoutSeconds = 5f;
        [SerializeField] private int maxBufferedBytes = 1024 * 1024;

        public TlsSettings Tls { get; } = new TlsSettings();

        public FomoxaRegistry Registry { get; set; }

        public override ITransportFactory CreateFactory()
        {
            var settings = new WebSocketSettings
            {
                Path = path,
                MaxHandshakeBytes = maxHandshakeBytes,
                HandshakeTimeout = TimeSpan.FromSeconds(handshakeTimeoutSeconds),
                Tls = Tls,
            };
            ITransportFactory webSocket = RunsInBrowser
                ? new BrowserWebSocketFactory(settings, maxBufferedBytes, new JsBrowserSocket(), () => MonotonicClock.Now)
                : new WebSocketTransportFactory(settings);
            return new EncryptedWebSocketTransportFactory(Registry ?? FomoxaRegistry.Default, webSocket);
        }
    }
}
