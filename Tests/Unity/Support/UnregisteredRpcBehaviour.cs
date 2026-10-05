using Fomoxa.Networking;

namespace Fomoxa.Unity.Tests.Support
{
    public sealed class UnregisteredRpcBehaviour : NetworkBehaviour
    {
        protected override void OnRegisterRpcs(NetworkRpcs rpc)
        {
            rpc.OnServer(nameof(Hop), peerId => { });
        }

        [NetworkRpc]
        private void Hop()
        {
        }
    }
}
