using System;
using System.Collections.Generic;
using Fomoxa.Net;

namespace Fomoxa.Networking.Transports
{
    public sealed class WebSocketSettings
    {
        private string path = "/";
        private int maxHandshakeBytes = 8 * 1024;
        private TimeSpan handshakeTimeout = TimeSpan.FromSeconds(5);
        private TlsSettings tls = new TlsSettings();

        public string Path
        {
            get => path;
            set => path = value != null && value.StartsWith("/", StringComparison.Ordinal) && value.IndexOfAny(new[] { ' ', '?', '#' }) < 0
                ? value
                : throw new ArgumentException("a WebSocket path starts with '/' and has no space, query or fragment", nameof(value));
        }

        public int MaxHandshakeBytes
        {
            get => maxHandshakeBytes;
            set => maxHandshakeBytes = value >= 256 ? value : throw new ArgumentOutOfRangeException(nameof(value), "a WebSocket handshake needs at least 256 bytes");
        }

        public TimeSpan HandshakeTimeout
        {
            get => handshakeTimeout;
            set => handshakeTimeout = value > TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(nameof(value), "the WebSocket handshake timeout is positive");
        }

        public List<string> AllowedOrigins { get; } = new List<string>();

        public TlsSettings Tls
        {
            get => tls;
            set => tls = value ?? throw new ArgumentNullException(nameof(value));
        }

        internal Func<TimeSpan> Clock { get; set; } = () => MonotonicClock.Now;

        internal Fomoxa.Net.WebSockets.WebSocketSettings ForPackage()
        {
            var settings = new Fomoxa.Net.WebSockets.WebSocketSettings
            {
                Path = Path,
                MaxHandshakeBytes = MaxHandshakeBytes,
                HandshakeTimeout = HandshakeTimeout,
            };
            settings.AllowedOrigins.AddRange(AllowedOrigins);
            return settings;
        }
    }
}
