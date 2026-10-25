using Fomoxa.Networking;
using UnityEngine;

namespace Fomoxa.Unity
{
    public abstract class NetworkAuthenticator : MonoBehaviour, IServerAuthenticator, IClientAuthenticator
    {
        public virtual void RegisterServer(ServerAuthentication server)
        {
        }

        public virtual void OnServerAuthenticating(ServerAuthenticationContext context)
        {
        }

        public virtual void RegisterClient(ClientAuthentication client)
        {
        }

        public virtual void OnClientAuthenticating(ClientAuthenticationContext context)
        {
        }
    }
}
