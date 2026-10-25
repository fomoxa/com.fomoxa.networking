namespace Fomoxa.Networking
{
    public interface IServerAuthenticator
    {
        void RegisterServer(ServerAuthentication server);

        void OnServerAuthenticating(ServerAuthenticationContext context);
    }
}
