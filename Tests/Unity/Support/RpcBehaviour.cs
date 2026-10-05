using System;
using System.Collections.Generic;
using Fomoxa.Networking;
using Fomoxa.Networking.Sessions;

namespace Fomoxa.Unity.Tests.Support
{
    public sealed class RpcBehaviour : NetworkBehaviour
    {
        public readonly List<string> Calls = new List<string>();

        public int Registrations { get; private set; }

        public SendResult Fire(uint value) => Fire(new RpcValue { Value = value });

        public SendResult Fire(RpcValue value) => SendServerRpc(RpcCodecs.Fire, value);

        public SendResult Open(uint value) => SendServerRpc(RpcCodecs.Open, new RpcValue { Value = value });

        public int Announce(uint value) => Announce(new RpcValue { Value = value });

        public int Announce(RpcValue value) => SendObserversRpc(RpcCodecs.Announce, value);

        public SendResult Whisper(ulong peerId, uint value) => Whisper(peerId, new RpcValue { Value = value });

        public SendResult Whisper(ulong peerId, RpcValue value) => SendTargetRpc(peerId, RpcCodecs.Whisper, value);

        protected override void OnRegisterRpcs(NetworkRpcs rpc)
        {
            Registrations++;
            rpc.OnServer<RpcValue>(RpcCodecs.Fire, (peerId, value) => Calls.Add($"Fire {peerId} {value.Value}"));
            rpc.OnServer<RpcValue>(RpcCodecs.Open, (peerId, value) => Calls.Add($"Open {peerId} {value.Value}"), requireOwnership: false);
            rpc.OnClient<RpcValue>(RpcCodecs.Announce, value =>
            {
                if (value.Value == RpcCodecs.ThrowingValue)
                {
                    throw new InvalidOperationException("Announce failed");
                }

                Calls.Add($"Announce {value.Value}");
            });
            rpc.OnClient<RpcValue>(RpcCodecs.Whisper, value => Calls.Add($"Whisper {value.Value}"));
        }
    }
}
