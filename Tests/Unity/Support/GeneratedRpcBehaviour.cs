using System.Collections.Generic;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking;

namespace Fomoxa.Unity.Tests.Support
{
    public partial class GeneratedRpcBehaviour : NetworkBehaviour
    {
        public const uint JumpId = 0x2000_0034;
        public const uint WaveId = 0x2000_0035;
        public const uint PingId = 0x2000_0036;
        public const uint ShoutId = 0x2000_0037;

        public readonly List<string> Calls = new List<string>();

        public SendResult RequestFire(uint value) => SendServerRpc(GeneratedValueNetAdapter.Instance, new GeneratedValue { Value = value });

        public SendResult RequestOpen(uint value) => SendServerRpc(GeneratedValueWideAdapter.Instance, new GeneratedValue { Value = value });

        public SendResult RequestJump() => SendServerRpc(nameof(Jump));

        public SendResult RequestWave() => SendServerRpc(nameof(Wave));

        public SendResult AnnounceTo(ulong peerId, uint value) => SendTargetRpc(peerId, GeneratedNoticeNetAdapter.Instance, new GeneratedNotice { Value = value });

        public int PingObservers() => SendObserversRpc(nameof(Ping));

        public int ShoutObservers() => SendObserversRpc(nameof(Shout));

        public static void Declare(RpcMessageIds rpcIds)
        {
            rpcIds.Set(typeof(GeneratedRpcBehaviour).FullName, nameof(Jump), JumpId);
            rpcIds.Set(typeof(GeneratedRpcBehaviour).FullName, nameof(Wave), WaveId);
            rpcIds.Set(typeof(GeneratedRpcBehaviour).FullName, nameof(Ping), PingId);
            rpcIds.Set(typeof(GeneratedRpcBehaviour).FullName, nameof(Shout), ShoutId);
        }

        protected override void OnRegisterRpcs(NetworkRpcs rpc)
        {
            rpc.OnClient(nameof(Shout), Shout);
        }

        [ServerRpc(Codec = "net")]
        private void Fire(ulong peerId, GeneratedValue value)
        {
            Calls.Add($"Fire {peerId} {value.Value}");
        }

        [ServerRpc(Codec = "wide", RequireOwnership = false)]
        private void Open(GeneratedValue value)
        {
            Calls.Add($"Open {value.Value}");
        }

        [ServerRpc]
        private void Jump(ulong peerId)
        {
            Calls.Add($"Jump {peerId}");
        }

        [ServerRpc(RequireOwnership = false)]
        private void Wave()
        {
            Calls.Add("Wave");
        }

        [ClientRpc]
        private void Announce(GeneratedNotice notice)
        {
            Calls.Add($"Announce {notice.Value}");
        }

        [ClientRpc]
        private void Ping()
        {
            Calls.Add("Ping");
        }

        [NetworkRpc]
        private void Shout()
        {
            Calls.Add("Shout");
        }
    }
}
