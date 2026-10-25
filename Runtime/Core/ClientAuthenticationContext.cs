using System;
using System.Threading;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;

namespace Fomoxa.Networking
{
    public sealed class ClientAuthenticationContext
    {
        private readonly ClientSession session;

        internal ClientAuthenticationContext(ClientSession session, CancellationToken cancellationToken, ReadOnlyMemory<byte> ticket)
        {
            this.session = session;
            CancellationToken = cancellationToken;
            Ticket = ticket;
        }

        public CancellationToken CancellationToken { get; }

        public ReadOnlyMemory<byte> Ticket { get; }

        public SendResult Send<T>(IMessageCodec<T> codec, T value)
        {
            if (codec == null)
            {
                throw new ArgumentNullException(nameof(codec));
            }

            return session.SendAuthentication(this, codec.MessageId, codec.Encode(value).Span);
        }
    }
}
