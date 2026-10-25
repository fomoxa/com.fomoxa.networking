namespace Fomoxa.Networking
{
    public interface IClientAuthenticator
    {
        void RegisterClient(ClientAuthentication client);

        void OnClientAuthenticating(ClientAuthenticationContext context);
    }
}
