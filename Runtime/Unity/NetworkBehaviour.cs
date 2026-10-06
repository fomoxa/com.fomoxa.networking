using System;
using System.Collections.Generic;
using System.ComponentModel;
using Fomoxa.Networking;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Sessions;
using UnityEngine;

namespace Fomoxa.Unity
{
    public abstract class NetworkBehaviour : MonoBehaviour
    {
        private Forwarder core;

        public NetworkObject NetworkObject { get; private set; }

        public byte BehaviourIndex => Core.BehaviourIndex;

        public bool IsPredicting => Core.IsPredicting;

        public bool IsReplaying => Core.IsReplaying;

        public int ReconcileCount => Core.ReconcileCount;

        internal EntityBehaviour Core => core ??= new Forwarder(this);

        internal IEnumerable<uint> ServerRpcIds => Core.ServerRpcIds;

        internal IEnumerable<uint> ClientRpcIds => Core.ClientRpcIds;

        internal StateSlot StateSlot => Core.StateSlot;

        internal InputSlot InputSlot => Core.InputSlot;

        public virtual void OnStartServer()
        {
        }

        public virtual void OnStopServer()
        {
        }

        public virtual void OnStartClient()
        {
        }

        public virtual void OnStopClient()
        {
        }

        public virtual void OnOwnerChangedServer(ulong previousOwnerId)
        {
        }

        public virtual void OnOwnerChangedClient(ulong previousOwnerId)
        {
        }

        protected virtual void OnHostVisibility(bool visible)
        {
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        protected virtual void OnRegisterGeneratedRpcs(NetworkRpcs rpc)
        {
        }

        protected virtual void OnRegisterRpcs(NetworkRpcs rpc)
        {
        }

        protected virtual void OnRegisterState(NetworkState state)
        {
        }

        protected virtual void OnRegisterInput(NetworkInput input)
        {
        }

        protected virtual void OnReconciled(uint tick)
        {
        }

        internal virtual void PrepareState()
        {
        }

        protected SendResult SendServerRpc<T>(IMessageCodec<T> codec, T value) => Core.ServerRpcFrom(codec, value);

        protected int SendObserversRpc<T>(IMessageCodec<T> codec, T value) => Core.ObserversRpcFrom(codec, value);

        protected SendResult SendTargetRpc<T>(ulong peerId, IMessageCodec<T> codec, T value) => Core.TargetRpcFrom(peerId, codec, value);

        protected SendResult SendServerRpc(string rpc) => Core.ServerRpcFrom(rpc);

        protected int SendObserversRpc(string rpc) => Core.ObserversRpcFrom(rpc);

        protected SendResult SendTargetRpc(ulong peerId, string rpc) => Core.TargetRpcFrom(peerId, rpc);

        internal void HostVisibility(bool visible) => OnHostVisibility(visible);

        internal void Attach(NetworkObject networkObject, byte behaviourIndex)
        {
            NetworkObject = networkObject;
            Core.Attach(networkObject, behaviourIndex);
            OnAttached();
        }

        internal virtual void OnAttached()
        {
        }

        internal void Register(RpcMessageIds rpcIds, MessageChannels channels, StateProtocol stateProtocol, InputRules inputRules) =>
            Core.Register(rpcIds, channels, stateProtocol, inputRules);

        internal void Reconciled(uint tick) => Core.Reconciled(tick);

        internal bool TryGetServerRpc(uint messageId, out ServerRpc rpc) => Core.TryGetServerRpc(messageId, out rpc);

        internal bool TryGetClientRpc(uint messageId, out ClientRpc rpc) => Core.TryGetClientRpc(messageId, out rpc);

        private sealed class Forwarder : EntityBehaviour
        {
            private readonly NetworkBehaviour owner;

            public Forwarder(NetworkBehaviour owner)
            {
                this.owner = owner;
            }

            internal override Type DeclaringType => owner.GetType();

            internal override string DisplayName => owner.name;

            public override void OnStartServer() => owner.OnStartServer();

            public override void OnStopServer() => owner.OnStopServer();

            public override void OnStartClient() => owner.OnStartClient();

            public override void OnStopClient() => owner.OnStopClient();

            public override void OnOwnerChangedServer(ulong previousOwnerId) => owner.OnOwnerChangedServer(previousOwnerId);

            public override void OnOwnerChangedClient(ulong previousOwnerId) => owner.OnOwnerChangedClient(previousOwnerId);

            internal override void PrepareState() => owner.PrepareState();

            protected override void OnRegisterGeneratedRpcs(NetworkRpcs rpc) => owner.OnRegisterGeneratedRpcs(rpc);

            protected override void OnRegisterRpcs(NetworkRpcs rpc) => owner.OnRegisterRpcs(rpc);

            protected override void OnRegisterState(NetworkState state) => owner.OnRegisterState(state);

            protected override void OnRegisterInput(NetworkInput input) => owner.OnRegisterInput(input);

            protected override void OnReconciled(uint tick) => owner.OnReconciled(tick);
        }
    }
}
