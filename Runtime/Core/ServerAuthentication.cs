using System;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;

namespace Fomoxa.Networking
{
    public sealed class ServerAuthentication
    {
        private readonly ServerSession session;

        internal ServerAuthentication(ServerSession session)
        {
            this.session = session;
        }

        public void On<T>(IMessageCodec<T> codec, Action<ServerAuthenticationContext, T> handler)
            where T : new()
        {
            if (codec == null)
            {
                throw new ArgumentNullException(nameof(codec));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            session.Dispatcher.RegisterAuthentication(codec.MessageId, (peerId, payload) =>
            {
                var value = new T();
                codec.Decode(payload, ref value);
                handler(session.AuthenticationContextOf(peerId), value);
            });
        }
    }
}
