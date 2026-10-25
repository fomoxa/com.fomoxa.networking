using System;
using System.Threading;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;

namespace Fomoxa.Networking
{
    public sealed class ServerAuthenticationContext
    {
        private readonly ServerSession session;

        internal ServerAuthenticationContext(ServerSession session, ulong peerId, CancellationToken cancellationToken)
        {
            this.session = session;
            PeerId = peerId;
            CancellationToken = cancellationToken;
        }

        public ulong PeerId { get; }

        public CancellationToken CancellationToken { get; }

        public SendResult Send<T>(IMessageCodec<T> codec, T value)
        {
            if (codec == null)
            {
                throw new ArgumentNullException(nameof(codec));
            }

            return session.SendAuthentication(this, codec.MessageId, codec.Encode(value).Span);
        }

        public bool Accept() => session.Accept(this);

        public bool Reject() => session.Reject(this);
    }
}
