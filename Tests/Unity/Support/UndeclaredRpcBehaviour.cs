using Fomoxa.Networking;

namespace Fomoxa.Unity.Tests.Support
{
    public sealed class UndeclaredRpcBehaviour : NetworkBehaviour
    {
        protected override void OnRegisterRpcs(NetworkRpcs rpc)
        {
            rpc.OnServer<RpcValue>(RpcCodecs.Undeclared, (peerId, value) => { });
        }
    }
}
