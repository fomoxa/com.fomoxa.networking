using System;
using Fomoxa.Net;
using Fomoxa.Networking.Transports;
using UnityEngine;

namespace Fomoxa.Unity
{
    public sealed class WebSocketNetworkTransport : NetworkTransport
    {
        [SerializeField] private string path = "/";
        [SerializeField] private int maxHandshakeBytes = 8 * 1024;
        [SerializeField] private float handshakeTimeoutSeconds = 5f;
        [SerializeField] private string[] allowedOrigins = Array.Empty<string>();
        [SerializeField] private int maxBufferedBytes = 1024 * 1024;

        public override ITransportFactory CreateFactory()
        {
            var settings = new WebSocketSettings
            {
                Path = path,
                MaxHandshakeBytes = maxHandshakeBytes,
                HandshakeTimeout = TimeSpan.FromSeconds(handshakeTimeoutSeconds),
            };
            settings.AllowedOrigins.AddRange(allowedOrigins);
            return RunsInBrowser
                ? new BrowserWebSocketFactory(settings, maxBufferedBytes, new JsBrowserSocket(), () => MonotonicClock.Now)
                : new WebSocketTransportFactory(settings);
        }
    }
}
