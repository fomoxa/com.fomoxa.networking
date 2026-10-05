using System.Collections.Generic;
using System.ComponentModel;
using System;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking;
using UnityEngine;

namespace Fomoxa.Unity
{
    public abstract class NetworkBehaviour : MonoBehaviour
    {
        private readonly Dictionary<uint, ServerRpc> serverRpcs = new Dictionary<uint, ServerRpc>();
        private readonly Dictionary<uint, ClientRpc> clientRpcs = new Dictionary<uint, ClientRpc>();
        private bool registered;

        public NetworkObject NetworkObject { get; private set; }

        public byte BehaviourIndex { get; private set; }

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

        internal IEnumerable<uint> ServerRpcIds => serverRpcs.Keys;

        internal IEnumerable<uint> ClientRpcIds => clientRpcs.Keys;

        internal StateSlot StateSlot { get; private set; }

        internal InputSlot InputSlot { get; private set; }

        public bool IsPredicting => InputSlot != null && InputSlot.Predicting;

        public bool IsReplaying => NetworkObject != null && NetworkObject.Client != null && NetworkObject.Client.IsReplaying;

        public int ReconcileCount { get; private set; }

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

        protected SendResult SendServerRpc<T>(IMessageCodec<T> codec, T value)
        {
            NetworkObject networkObject = SpawnedOn(codec, value, isServer: false);
            return networkObject.Client.SendToObject(codec.MessageId, networkObject.ObjectId, BehaviourIndex, codec.Encode(value).Span);
        }

        protected int SendObserversRpc<T>(IMessageCodec<T> codec, T value)
        {
            NetworkObject networkObject = SpawnedOn(codec, value, isServer: true);
            return networkObject.Server.BroadcastToObject(codec.MessageId, networkObject.ObjectId, BehaviourIndex, codec.Encode(value).Span);
        }

        protected SendResult SendTargetRpc<T>(ulong peerId, IMessageCodec<T> codec, T value)
        {
            NetworkObject networkObject = SpawnedOn(codec, value, isServer: true);
            return networkObject.Server.SendToObserver(peerId, codec.MessageId, networkObject.ObjectId, BehaviourIndex, codec.Encode(value).Span);
        }

        protected SendResult SendServerRpc(string rpc)
        {
            NetworkObject networkObject = SpawnedOn(rpc, isServer: false);
            return networkObject.Client.SendToObject(RpcMessageId(networkObject.Client.RpcIds, rpc), networkObject.ObjectId, BehaviourIndex, ReadOnlySpan<byte>.Empty);
        }

        protected int SendObserversRpc(string rpc)
        {
            NetworkObject networkObject = SpawnedOn(rpc, isServer: true);
            return networkObject.Server.BroadcastToObject(RpcMessageId(networkObject.Server.RpcIds, rpc), networkObject.ObjectId, BehaviourIndex, ReadOnlySpan<byte>.Empty);
        }

        protected SendResult SendTargetRpc(ulong peerId, string rpc)
        {
            NetworkObject networkObject = SpawnedOn(rpc, isServer: true);
            return networkObject.Server.SendToObserver(peerId, RpcMessageId(networkObject.Server.RpcIds, rpc), networkObject.ObjectId, BehaviourIndex, ReadOnlySpan<byte>.Empty);
        }

        internal void HostVisibility(bool visible) => OnHostVisibility(visible);

        internal void Attach(NetworkObject networkObject, byte behaviourIndex)
        {
            NetworkObject = networkObject;
            BehaviourIndex = behaviourIndex;
        }

        internal void Register(RpcMessageIds rpcIds, MessageChannels channels, StateProtocol stateProtocol, InputRules inputRules)
        {
            if (registered)
            {
                return;
            }

            registered = true;
            var rpcs = new NetworkRpcs(this, rpcIds);
            OnRegisterGeneratedRpcs(rpcs);
            OnRegisterRpcs(rpcs);
            OnRegisterState(new NetworkState(this, channels, stateProtocol));
            OnRegisterInput(new NetworkInput(this, inputRules));
        }

        internal void SetState(StateSlot slot)
        {
            if (StateSlot != null)
            {
                throw new HandlerRegistrationException($"{GetType().FullName} uses more than one state model");
            }

            StateSlot = slot;
        }

        internal void Reconciled(uint tick)
        {
            ReconcileCount++;
            OnReconciled(tick);
        }

        internal void SetInput(InputSlot slot)
        {
            if (InputSlot != null)
            {
                throw new HandlerRegistrationException($"{GetType().FullName} uses more than one input model");
            }

            InputSlot = slot;
        }

        internal void AddServerRpc(uint messageId, ServerRpc rpc)
        {
            if (!serverRpcs.TryAdd(messageId, rpc))
            {
                throw new HandlerRegistrationException($"{GetType().FullName} registers message id 0x{messageId:X8} as a server RPC twice");
            }
        }

        internal void AddClientRpc(uint messageId, ClientRpc rpc)
        {
            if (!clientRpcs.TryAdd(messageId, rpc))
            {
                throw new HandlerRegistrationException($"{GetType().FullName} registers message id 0x{messageId:X8} as a client RPC twice");
            }
        }

        internal bool TryGetServerRpc(uint messageId, out ServerRpc rpc) => serverRpcs.TryGetValue(messageId, out rpc);

        internal bool TryGetClientRpc(uint messageId, out ClientRpc rpc) => clientRpcs.TryGetValue(messageId, out rpc);

        private NetworkObject SpawnedOn<T>(IMessageCodec<T> codec, T value, bool isServer)
        {
            if (codec == null)
            {
                throw new ArgumentNullException(nameof(codec));
            }

            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return SpawnedOn(isServer);
        }

        private NetworkObject SpawnedOn(string rpc, bool isServer)
        {
            if (rpc == null)
            {
                throw new ArgumentNullException(nameof(rpc));
            }

            return SpawnedOn(isServer);
        }

        private NetworkObject SpawnedOn(bool isServer)
        {
            NetworkObject networkObject = NetworkObject;
            bool spawned = networkObject != null && (isServer ? networkObject.Server != null : networkObject.Client != null);
            if (!spawned)
            {
                throw new InvalidOperationException($"{name} is not spawned on the {(isServer ? "server" : "client")}");
            }

            return networkObject;
        }

        private uint RpcMessageId(RpcMessageIds rpcIds, string rpc)
        {
            if (!rpcIds.TryGet(GetType(), rpc, out uint messageId))
            {
                throw new InvalidOperationException(NetworkRpcs.NotGenerated(GetType(), rpc));
            }

            return messageId;
        }
    }
}
