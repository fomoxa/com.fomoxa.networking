using Fomoxa.Networking;

namespace Fomoxa.Unity.Tests.Support
{
    public sealed partial class DerivedGeneratedRpcBehaviour : GeneratedRpcBehaviour
    {
        public const uint CheerId = 0x2000_0038;

        public int CheerObservers() => SendObserversRpc(nameof(Cheer));

        public static void DeclareDerived(RpcMessageIds rpcIds)
        {
            rpcIds.Set(typeof(DerivedGeneratedRpcBehaviour).FullName, nameof(Cheer), CheerId);
        }

        [ClientRpc]
        private void Cheer()
        {
            Calls.Add("Cheer");
        }
    }
}
