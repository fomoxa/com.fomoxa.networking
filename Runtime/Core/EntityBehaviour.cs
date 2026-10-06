using System;
using System.Collections.Generic;
using System.ComponentModel;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Sessions;

namespace Fomoxa.Networking
{
    public abstract class EntityBehaviour
    {
        private const int MaxBehaviours = 256;

        private readonly Dictionary<uint, ServerRpc> serverRpcs = new Dictionary<uint, ServerRpc>();
        private readonly Dictionary<uint, ClientRpc> clientRpcs = new Dictionary<uint, ClientRpc>();
        private IBehaviourLink link;
        private bool registered;

        public byte BehaviourIndex { get; private set; }

        public bool IsPredicting => InputSlot != null && InputSlot.Predicting;

        public bool IsReplaying => link != null && link.IsReplaying;

        public int ReconcileCount { get; private set; }

        internal IEnumerable<uint> ServerRpcIds => serverRpcs.Keys;

        internal IEnumerable<uint> ClientRpcIds => clientRpcs.Keys;

        internal StateSlot StateSlot { get; private set; }

        internal InputSlot InputSlot { get; private set; }

        internal TransformSlot TransformSlot { get; private set; }

        internal bool SpawnedOnServer => link != null && link.SpawnedOnServer;

        internal virtual Type DeclaringType => GetType();

        internal virtual string DisplayName => DeclaringType.Name;

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

        protected void SyncTransform(TransformSync sync, ITransformSource source, ITransformReceiver receiver) => SetTransform(sync, source, receiver);

        protected SendResult SendServerRpc<T>(IMessageCodec<T> codec, T value) => ServerRpcFrom(codec, value);

        protected int SendObserversRpc<T>(IMessageCodec<T> codec, T value) => ObserversRpcFrom(codec, value);

        protected SendResult SendTargetRpc<T>(ulong peerId, IMessageCodec<T> codec, T value) => TargetRpcFrom(peerId, codec, value);

        protected SendResult SendServerRpc(string rpc) => ServerRpcFrom(rpc);

        protected int SendObserversRpc(string rpc) => ObserversRpcFrom(rpc);

        protected SendResult SendTargetRpc(ulong peerId, string rpc) => TargetRpcFrom(peerId, rpc);

        internal SendResult ServerRpcFrom<T>(IMessageCodec<T> codec, T value)
        {
            IBehaviourLink target = SpawnedOn(codec, value, isServer: false);
            return target.SendToServer(codec.MessageId, BehaviourIndex, codec.Encode(value).Span);
        }

        internal int ObserversRpcFrom<T>(IMessageCodec<T> codec, T value)
        {
            IBehaviourLink target = SpawnedOn(codec, value, isServer: true);
            return target.SendToObservers(codec.MessageId, BehaviourIndex, codec.Encode(value).Span);
        }

        internal SendResult TargetRpcFrom<T>(ulong peerId, IMessageCodec<T> codec, T value)
        {
            IBehaviourLink target = SpawnedOn(codec, value, isServer: true);
            return target.SendToObserver(peerId, codec.MessageId, BehaviourIndex, codec.Encode(value).Span);
        }

        internal SendResult ServerRpcFrom(string rpc)
        {
            IBehaviourLink target = SpawnedOn(rpc, isServer: false);
            return target.SendToServer(RpcMessageId(target, isServer: false, rpc), BehaviourIndex, ReadOnlySpan<byte>.Empty);
        }

        internal int ObserversRpcFrom(string rpc)
        {
            IBehaviourLink target = SpawnedOn(rpc, isServer: true);
            return target.SendToObservers(RpcMessageId(target, isServer: true, rpc), BehaviourIndex, ReadOnlySpan<byte>.Empty);
        }

        internal SendResult TargetRpcFrom(ulong peerId, string rpc)
        {
            IBehaviourLink target = SpawnedOn(rpc, isServer: true);
            return target.SendToObserver(peerId, RpcMessageId(target, isServer: true, rpc), BehaviourIndex, ReadOnlySpan<byte>.Empty);
        }

        public static void Attach(INetworkEntity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            IReadOnlyList<EntityBehaviour> behaviours = entity.EntityBehaviours;
            if (behaviours.Count > MaxBehaviours)
            {
                throw new ArgumentException($"{behaviours.Count} behaviours exceed the limit of {MaxBehaviours}", nameof(entity));
            }

            for (int index = 0; index < behaviours.Count; index++)
            {
                if (behaviours[index] == null)
                {
                    throw new ArgumentException($"behaviour {index} is null", nameof(entity));
                }
            }

            var link = new EntityLink(entity);
            for (int index = 0; index < behaviours.Count; index++)
            {
                behaviours[index].Attach(link, (byte)index);
            }
        }

        internal void Attach(IBehaviourLink behaviourLink, byte behaviourIndex)
        {
            link = behaviourLink;
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
                throw new HandlerRegistrationException($"{DeclaringType.FullName} uses more than one state model");
            }

            StateSlot = slot;
        }

        internal void SetInput(InputSlot slot)
        {
            if (InputSlot != null)
            {
                throw new HandlerRegistrationException($"{DeclaringType.FullName} uses more than one input model");
            }

            InputSlot = slot;
        }

        internal void SetTransform(TransformSync sync, ITransformSource source, ITransformReceiver receiver)
        {
            if (sync == null)
            {
                throw new ArgumentNullException(nameof(sync));
            }

            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (receiver == null)
            {
                throw new ArgumentNullException(nameof(receiver));
            }

            if (TransformSlot != null)
            {
                throw new HandlerRegistrationException($"{DeclaringType.FullName} synchronizes more than one transform");
            }

            sync.Bind(this);
            TransformSlot = new TransformSlot(sync, source, receiver);
        }

        internal void Reconciled(uint tick)
        {
            ReconcileCount++;
            OnReconciled(tick);
        }

        internal void AddServerRpc(uint messageId, ServerRpc rpc)
        {
            if (!serverRpcs.TryAdd(messageId, rpc))
            {
                throw new HandlerRegistrationException($"{DeclaringType.FullName} registers message id 0x{messageId:X8} as a server RPC twice");
            }
        }

        internal void AddClientRpc(uint messageId, ClientRpc rpc)
        {
            if (!clientRpcs.TryAdd(messageId, rpc))
            {
                throw new HandlerRegistrationException($"{DeclaringType.FullName} registers message id 0x{messageId:X8} as a client RPC twice");
            }
        }

        internal bool TryGetServerRpc(uint messageId, out ServerRpc rpc) => serverRpcs.TryGetValue(messageId, out rpc);

        internal bool TryGetClientRpc(uint messageId, out ClientRpc rpc) => clientRpcs.TryGetValue(messageId, out rpc);

        private IBehaviourLink SpawnedOn<T>(IMessageCodec<T> codec, T value, bool isServer)
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

        private IBehaviourLink SpawnedOn(string rpc, bool isServer)
        {
            if (rpc == null)
            {
                throw new ArgumentNullException(nameof(rpc));
            }

            return SpawnedOn(isServer);
        }

        private IBehaviourLink SpawnedOn(bool isServer)
        {
            bool spawned = link != null && (isServer ? link.SpawnedOnServer : link.SpawnedOnClient);
            if (!spawned)
            {
                throw new InvalidOperationException($"{DisplayName} is not spawned on the {(isServer ? "server" : "client")}");
            }

            return link;
        }

        private uint RpcMessageId(IBehaviourLink target, bool isServer, string rpc)
        {
            if (!target.RpcIds(isServer).TryGet(DeclaringType, rpc, out uint messageId))
            {
                throw new InvalidOperationException(NetworkRpcs.NotGenerated(DeclaringType, rpc));
            }

            return messageId;
        }
    }
}
