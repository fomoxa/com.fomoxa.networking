using System.Collections.Generic;
using Fomoxa.Networking;

namespace Fomoxa.Unity.Tests.Support
{
    public sealed class TestAuthenticator : NetworkAuthenticator
    {
        public readonly List<ServerAuthenticationContext> ServerContexts = new List<ServerAuthenticationContext>();

        public readonly List<ClientAuthenticationContext> ClientContexts = new List<ClientAuthenticationContext>();

        public override void OnServerAuthenticating(ServerAuthenticationContext context) => ServerContexts.Add(context);

        public override void OnClientAuthenticating(ClientAuthenticationContext context) => ClientContexts.Add(context);
    }
}
