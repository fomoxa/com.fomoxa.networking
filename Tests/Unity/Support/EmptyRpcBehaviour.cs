using System.Collections.Generic;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking;

namespace Fomoxa.Unity.Tests.Support
{
    public class EmptyRpcBehaviour : NetworkBehaviour
    {
        public const uint JumpId = 0x2000_0021;
        public const uint WaveId = 0x2000_0022;
        public const uint PingId = 0x2000_0023;

        public readonly List<string> Calls = new List<string>();

        public SendResult RequestJump() => SendServerRpc(nameof(Jump));

        public SendResult RequestWave() => SendServerRpc(nameof(Wave));

        public int PingObservers() => SendObserversRpc(nameof(Ping));

        public SendResult PingTarget(ulong peerId) => SendTargetRpc(peerId, nameof(Ping));

        public SendResult SendServerRpcNamed(string rpc) => SendServerRpc(rpc);

        public int SendObserversRpcNamed(string rpc) => SendObserversRpc(rpc);

        public SendResult SendTargetRpcNamed(ulong peerId, string rpc) => SendTargetRpc(peerId, rpc);

        public static void Declare(RpcMessageIds rpcIds)
        {
            rpcIds.Set(typeof(EmptyRpcBehaviour).FullName, nameof(Jump), JumpId);
            rpcIds.Set(typeof(EmptyRpcBehaviour).FullName, nameof(Wave), WaveId);
            rpcIds.Set(typeof(EmptyRpcBehaviour).FullName, nameof(Ping), PingId);
        }

        protected override void OnRegisterRpcs(NetworkRpcs rpc)
        {
            rpc.OnServer(nameof(Jump), peerId => Calls.Add($"Jump {peerId}"));
            rpc.OnServer(nameof(Wave), peerId => Calls.Add($"Wave {peerId}"), requireOwnership: false);
            rpc.OnClient(nameof(Ping), Ping);
        }

        [NetworkRpc]
        private void Jump()
        {
        }

        [NetworkRpc(Channel = Channel.Unreliable)]
        private void Wave()
        {
        }

        [NetworkRpc]
        private void Ping()
        {
            Calls.Add("Ping");
        }
    }
}
