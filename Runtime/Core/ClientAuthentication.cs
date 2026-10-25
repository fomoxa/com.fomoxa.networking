using System;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;

namespace Fomoxa.Networking
{
    public sealed class ClientAuthentication
    {
        private readonly ClientSession session;

        internal ClientAuthentication(ClientSession session)
        {
            this.session = session;
        }

        public void On<T>(IMessageCodec<T> codec, Action<ClientAuthenticationContext, T> handler)
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
                handler(session.AuthenticationContext, value);
            });
        }
    }
}
