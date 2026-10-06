using System;
using System.Collections.Generic;
using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Prediction;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Timing;
using Fomoxa.Networking.Transports;

namespace Fomoxa.Networking
{
    public sealed class ServerManager
    {
        private readonly ITransportFactory transport;
        private readonly int loopbackCapacity;
        private readonly ServerSession session;
        private readonly MessageChannels channels;
        private readonly StateProtocol stateProtocol;
        private readonly IServerSceneHost sceneHost;
        private readonly NetworkLog log;

        internal ServerManager(
            Schema schema,
            SessionLimits limits,
            SessionConfig sessionConfig,
            SessionProtocol protocol,
            ObjectProtocol objectProtocol,
            StateProtocol stateProtocol,
            TransformProtocol transformProtocol,
            SceneProtocol sceneProtocol,
            ClockProtocol clockProtocol,
            InputProtocol inputProtocol,
            RpcMessageIds rpcIds,
            ITransportFactory transport,
            int loopbackCapacity,
            IServerEntityBackend entityBackend,
            IServerSceneHost sceneHost,
            NetworkLog log)
        {
            this.transport = transport;
            this.loopbackCapacity = loopbackCapacity;
            this.sceneHost = sceneHost;
            this.log = log;
            EntityBackend = entityBackend;
            channels = protocol.Channels;
            this.stateProtocol = stateProtocol;
            RpcIds = rpcIds;
            Dispatcher = new MessageDispatcher(schema);
            session = new ServerSession(schema, sessionConfig, limits, Dispatcher, protocol);
            session.OnHandlerException += RaiseHandlerException;
            Objects = new ServerObjects(session, objectProtocol, objectId => Entities.ReadSpawnData(objectId), sceneProtocol);
            Clock = new ServerClock(session, clockProtocol);
            Inputs = new ServerInputs(session, Objects, inputProtocol, Clock);
            Entities = new ServerEntities(this, session, Objects, Inputs, Dispatcher, channels, stateProtocol, inputProtocol, transformProtocol, rpcIds, entityBackend, log);
            Scenes = new NetworkScenes(this);
            SceneContent = new ServerSceneContent(session, Objects.Scenes, Entities, sceneHost, log);
            SceneContent.OnLoaded += sceneId => OnNetworkSceneReady?.Invoke(sceneId);
            SceneContent.OnLoaded += Scenes.RaiseLoaded;
            SceneContent.OnLoadFailed += (sceneId, exception) => OnNetworkSceneFailed?.Invoke(sceneId);
            SceneContent.OnLoadFailed += Scenes.RaiseLoadFailed;
        }

        public event Action<ServerConnectionStateArgs> OnServerConnectionState
        {
            add => session.OnServerConnectionState += value;
            remove => session.OnServerConnectionState -= value;
        }

        public event Action<ConnectionStateArgs> OnRemoteConnectionState
        {
            add => session.OnRemoteConnectionState += value;
            remove => session.OnRemoteConnectionState -= value;
        }

        public event Action<SendDroppedArgs> OnSendDropped
        {
            add => session.OnSendDropped += value;
            remove => session.OnSendDropped -= value;
        }

        public event Action<ReceiveDroppedArgs> OnReceiveDropped
        {
            add => session.OnReceiveDropped += value;
            remove => session.OnReceiveDropped -= value;
        }

        public event Action<HandlerExceptionArgs> OnHandlerException;

        public MessageDispatcher Dispatcher { get; }

        public ServerState State => session.State;

        public bool HasRtt(ulong peerId) => Clock.HasRtt(peerId);

        public TimeSpan RttOf(ulong peerId) => Clock.RttOf(peerId);

        public int DroppedInputsOf(ulong peerId) => Inputs.DroppedOf(peerId);

        public int PeerCount => session.PeerCount;

        public int FrameBudgetOf(ulong peerId) => session.FrameBudgetOf(peerId);

        public ushort Port { get; private set; }

        public IReadOnlyDictionary<uint, INetworkEntity> Spawned => Entities.Representations;

        public IObserverRule ObserverRule
        {
            get => Entities.ObserverRule;
            set => Entities.ObserverRule = value;
        }

        public NetworkScenes Scenes { get; }

        public int ObserverInterval
        {
            get => Objects.ObserverInterval;
            set => Objects.ObserverInterval = value;
        }

        internal LoopbackListener LocalListener { get; private set; }

        internal RpcMessageIds RpcIds { get; }

        internal ServerObjects Objects { get; }

        internal ServerClock Clock { get; }

        internal ServerInputs Inputs { get; }

        internal InputRules InputRules
        {
            get => Entities.InputRules;
            set => Entities.InputRules = value;
        }

        internal ServerEntities Entities { get; }

        internal ServerSceneContent SceneContent { get; }

        internal IServerEntityBackend EntityBackend { get; }

        internal IServerSceneHost SceneHost => sceneHost;

        internal event Action<uint> OnNetworkSceneReady;

        internal event Action<uint> OnNetworkSceneFailed;

        public ConnectionState PeerState(ulong peerId) => session.PeerState(peerId);

        public void StartConnection(ushort port)
        {
            if (session.State != ServerState.Stopped)
            {
                throw new InvalidOperationException("the server is already running");
            }

            IListenerTransport network = transport.CreateListener(port, out ushort boundPort);
            var localListener = new LoopbackListener(loopbackCapacity);
            LocalListener = localListener;
            Port = boundPort;
            session.Start(new CompositeListener(new[] { network, localListener }, new[] { transport.FrameBudget, transport.FrameBudget }));
            SpawnSceneObjects();
        }

        public void StopConnection()
        {
            session.Stop();
            LocalListener = null;
            Port = 0;
        }

        public SendResult Send(ulong peerId, uint messageId, ReadOnlySpan<byte> payload) =>
            session.Send(peerId, messageId, payload);

        public SendResult SendToObject(ulong peerId, uint messageId, uint objectId, byte behaviourIndex, ReadOnlySpan<byte> body) =>
            session.SendToObject(peerId, messageId, objectId, behaviourIndex, body);

        public int Broadcast(uint messageId, ReadOnlySpan<byte> payload) => session.Broadcast(messageId, payload);

        public void Disconnect(ulong peerId) => session.Disconnect(peerId);

        public void Spawn(INetworkEntity networkObject, ulong ownerId = 0)
        {
            if (networkObject == null)
            {
                throw new ArgumentNullException(nameof(networkObject));
            }

            Entities.Spawn(networkObject, ownerId);
        }

        public bool Despawn(INetworkEntity networkObject)
        {
            if (networkObject == null)
            {
                throw new ArgumentNullException(nameof(networkObject));
            }

            return Entities.Despawn(networkObject);
        }

        public bool ChangeOwner(INetworkEntity networkObject, ulong ownerId)
        {
            if (networkObject == null)
            {
                throw new ArgumentNullException(nameof(networkObject));
            }

            return Entities.ChangeOwner(networkObject, ownerId);
        }

        public bool IsObserver(INetworkEntity networkObject, ulong peerId)
        {
            if (networkObject == null)
            {
                throw new ArgumentNullException(nameof(networkObject));
            }

            return Entities.IsObserver(networkObject, peerId);
        }

        public bool RebuildObservers(INetworkEntity networkObject)
        {
            if (networkObject == null)
            {
                throw new ArgumentNullException(nameof(networkObject));
            }

            return Entities.RebuildObservers(networkObject);
        }

        public void RebuildObserversOfPeer(ulong peerId) => Entities.RebuildObserversOfPeer(peerId);

        public void RebuildObservers() => Entities.RebuildObservers();

        internal int BroadcastToObject(uint messageId, uint objectId, byte behaviourIndex, ReadOnlySpan<byte> body) =>
            Objects.SendToObservers(messageId, objectId, behaviourIndex, body);

        internal SendResult SendToObserver(ulong peerId, uint messageId, uint objectId, byte behaviourIndex, ReadOnlySpan<byte> body) =>
            Objects.SendToObserver(peerId, messageId, objectId, behaviourIndex, body);

        internal void RebuildObserversRound() => Entities.RebuildObserversRound();

        internal bool IsLoadingNetworkScene(uint sceneId) => SceneContent.IsLoading(sceneId);

        internal void Tick(TimeSpan now)
        {
            Entities.NextAnchorRound();
            session.Tick(now);
        }

        internal void Flush() => session.Flush();

        internal void ApplyInputs(uint tick, ulong hostPeerId) => Entities.ApplyInputs(tick, hostPeerId);

        internal void SendReconcileStates(uint tick) => Entities.SendReconcileStates(tick);

        internal void DespawnDestroyed(INetworkEntity entity) => Entities.DespawnDestroyed(entity);

        private void SpawnSceneObjects()
        {
            Entities.ResetSceneObjects();
            var found = new List<INetworkEntity>();
            sceneHost.PresentSceneObjects(found);
            Entities.SpawnSceneObjects(found);
        }

        internal void SyncStates() => Entities.SyncStates();

        internal void SyncTransforms(uint tick) => Entities.SyncTransforms(tick);

        private void RaiseHandlerException(HandlerExceptionArgs args)
        {
            Action<HandlerExceptionArgs> handlers = OnHandlerException;
            if (handlers == null)
            {
                log.Exception(args.Exception);
                return;
            }

            handlers(args);
        }
    }
}
