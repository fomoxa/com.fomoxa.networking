using Fomoxa.Networking;

namespace Fomoxa.Unity.Tests.Support
{
    public sealed class ConflictingRpcBehaviour : NetworkBehaviour
    {
        protected override void OnRegisterRpcs(NetworkRpcs rpc)
        {
            rpc.OnServer<RpcValue>(RpcCodecs.Fire, (peerId, value) => { });
        }
    }
}
