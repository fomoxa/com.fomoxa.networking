using System;
using Fomoxa.Net;

namespace Fomoxa.Networking.Transports
{
    public sealed class TlsSettings
    {
        private ITlsCertificateCheck certificateCheck = new TlsCertificateCheck(TlsRoots.Mozilla);
        private TimeSpan handshakeTimeout = TimeSpan.FromSeconds(5);

        public ITlsCertificateCheck CertificateCheck
        {
            get => certificateCheck;
            set => certificateCheck = value ?? throw new ArgumentNullException(nameof(value));
        }

        public TimeSpan HandshakeTimeout
        {
            get => handshakeTimeout;
            set => handshakeTimeout = value > TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(nameof(value), "the TLS handshake timeout is positive");
        }

        internal Func<TimeSpan> Clock { get; set; } = () => MonotonicClock.Now;
    }
}
