using System;
using System.Collections.Generic;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Prediction;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Timing;
using Fomoxa.Networking.Transports;

namespace Fomoxa.Networking
{
    public sealed class ClientManager
    {
        private readonly ITransportFactory transport;
        private readonly ServerManager serverManager;
        private readonly ClientSession session;
        private readonly ClientReconnector reconnector;
        private readonly ISceneHost sceneHost;
        private readonly Func<TimeSpan> clockSource;
        private readonly NetworkLog log;
        private readonly Dictionary<uint, HostSceneWait> hostWaits = new Dictionary<uint, HostSceneWait>();
        private readonly MessageChannels channels;
        private readonly StateProtocol stateProtocol;
        private readonly TimeManager timeManager;
        private readonly ClientClock clock;

        internal ClientManager(
            Schema schema,
            SessionLimits limits,
            SessionConfig sessionConfig,
            SessionProtocol protocol,
            ObjectProtocol objectProtocol,
            StateProtocol stateProtocol,
            TransformProtocol transformProtocol,
            SceneProtocol sceneProtocol,
            ClockProtocol clockProtocol,
            ClockSettings clockSettings,
            InputProtocol inputProtocol,
            TimeManager timeManager,
            RpcMessageIds rpcIds,
            ReconnectPolicy reconnectPolicy,
            ITransportFactory transport,
            ServerManager serverManager,
            IClientEntityBackend entityBackend,
            ISceneHost sceneHost,
            IClientPredictionBackend predictionBackend,
            Func<TimeSpan> clockSource,
            NetworkLog log)
        {
            this.transport = transport;
            this.serverManager = serverManager;
            this.sceneHost = sceneHost;
            this.clockSource = clockSource;
            this.log = log;
            EntityBackend = entityBackend;
            PredictionBackend = predictionBackend;
            channels = protocol.Channels;
            this.stateProtocol = stateProtocol;
            this.timeManager = timeManager;
            RpcIds = rpcIds;
            Dispatcher = new MessageDispatcher(schema);
            session = new ClientSession(schema, sessionConfig, limits, Dispatcher, protocol);
            session.OnHandlerException += RaiseHandlerException;
            Entities = new ClientEntities(this, session, Dispatcher, channels, stateProtocol, transformProtocol, rpcIds, serverManager.Entities, entityBackend, log);
            Prediction = new ClientPrediction(Entities, session, Dispatcher, inputProtocol, predictionBackend, log);
            session.OnClientConnectionState += PrepareSceneObjectsWhenStarted;
            session.OnClientConnectionState += EndHostVisibilityWhenStopped;
            reconnector = new ClientReconnector(session, reconnectPolicy);
            Objects = new ClientObjects(session, objectProtocol, Entities, sceneProtocol, new HostAwareSceneHost(this));
            Entities.Attach(Objects);
            Objects.OnObjectMismatch += RaiseObjectMismatch;
            Objects.OnLocalPeerAssigned += HideUnobservedOnHost;
            Objects.OnLocalPeerAssigned += FollowServerTickRate;
            clock = new ClientClock(session, clockProtocol, clockSettings);
            session.OnClientConnectionState += RestoreTickRateWhenStopped;
            serverManager.Entities.OnSpawned += HideIfUnobservedOnHost;
            serverManager.OnNetworkSceneReady += sceneId => FinishHostWait(sceneId, true);
            serverManager.OnNetworkSceneFailed += sceneId => FinishHostWait(sceneId, false);
            serverManager.OnServerConnectionState += FailHostWaitsWhenStopped;
        }

        public event Action<ConnectionStateArgs> OnClientConnectionState
        {
            add => reconnector.OnClientConnectionState += value;
            remove => reconnector.OnClientConnectionState -= value;
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

        public event Action<ObjectMismatchArgs> OnObjectMismatch;

        public MessageDispatcher Dispatcher { get; }

        public ConnectionState State => session.State;

        public bool IsReplaying => Prediction.IsReplaying;

        public TimeSpan Rtt => connectedLocally ? TimeSpan.Zero : clock.Estimator.Rtt;

        internal int TickRate => timeManager.TickRate;

        public IReadOnlyDictionary<uint, INetworkEntity> Spawned => Entities.Representations;

        internal ClientObjects Objects { get; }

        internal ClockEstimator Clock => clock.Estimator;

        internal bool ConnectedLocally => connectedLocally;

        internal ClientEntities Entities { get; }

        private bool connectedLocally => Entities.ConnectedLocally;

        internal InputRules InputRules
        {
            get => Entities.InputRules;
            set => Entities.InputRules = value;
        }

        internal IClientEntityBackend EntityBackend { get; }

        internal IClientPredictionBackend PredictionBackend { get; }

        internal bool SimulatesPhysics => Prediction.SimulatesPhysics;

        internal ClientPrediction Prediction { get; }

        internal bool RecordsContacts => !connectedLocally && session.State == ConnectionState.Started;

        internal RpcMessageIds RpcIds { get; }

        public void StartConnection(string address, ushort port)
        {
            if (session.State != ConnectionState.Stopped)
            {
                throw new InvalidOperationException("the client is already running");
            }

            if (serverManager.State == ServerState.Started)
            {
                Entities.ConnectedLocally = true;
                reconnector.StartWithoutRetry(serverManager.LocalListener.Connect(), clockSource());
                return;
            }

            Entities.ConnectedLocally = false;
            reconnector.Start(() => transport.CreateConnector(address, port), clockSource());
        }

        public void StopConnection() => reconnector.Stop();

        public SendResult Send(uint messageId, ReadOnlySpan<byte> payload) => session.Send(messageId, payload);

        public SendResult SendToObject(uint messageId, uint objectId, byte behaviourIndex, ReadOnlySpan<byte> body) =>
            session.SendToObject(messageId, objectId, behaviourIndex, body);

        internal void Tick(TimeSpan now) => reconnector.Tick(now);

        internal void PlaceProxies() => Prediction.PlaceProxies();

        internal void EndProxies() => Prediction.EndProxies();

        internal void Predict(uint predictionTick) => Prediction.Predict(predictionTick, timeManager.ClockSynced, (float)timeManager.TickDelta);

        internal void CapturePredicted(uint predictionTick) => Prediction.CapturePredicted(predictionTick);

        internal void UpdateClock(TimeSpan now)
        {
            if (!connectedLocally)
            {
                clock.Update(now);
            }
        }

        internal void Flush() => session.Flush();

        internal void EndShared(INetworkEntity shared) => Entities.EndShared(shared.Record);

        internal bool ForgetDestroyed(INetworkEntity entity) => Entities.ForgetDestroyed(entity);

        private void PrepareSceneObjectsWhenStarted(ConnectionStateArgs args)
        {
            if (args.State != ConnectionState.Started || connectedLocally)
            {
                return;
            }

            EntityBackend.PrepareSceneObjects();
        }

        private bool LoadScene(uint sceneId, Action loaded, Action failed)
        {
            if (!connectedLocally)
            {
                return sceneHost.TryLoad(sceneId, loaded, failed);
            }

            if (serverManager.SceneContent.IsLoaded(sceneId))
            {
                loaded();
            }
            else if (serverManager.IsLoadingNetworkScene(sceneId))
            {
                hostWaits[sceneId] = new HostSceneWait(loaded, failed);
            }
            else
            {
                failed();
            }

            return true;
        }

        private void UnloadScene(uint sceneId, Action unloaded)
        {
            if (connectedLocally)
            {
                unloaded();
                return;
            }

            sceneHost.Unload(sceneId, unloaded);
        }

        private void FinishHostWait(uint sceneId, bool succeeded)
        {
            if (!hostWaits.Remove(sceneId, out HostSceneWait wait))
            {
                return;
            }

            if (succeeded)
            {
                wait.Loaded();
            }
            else
            {
                wait.Failed();
            }
        }

        private void FailHostWaitsWhenStopped(ServerConnectionStateArgs args)
        {
            if (args.State != ServerState.Stopped || hostWaits.Count == 0)
            {
                return;
            }

            var waits = new List<HostSceneWait>(hostWaits.Values);
            hostWaits.Clear();
            foreach (HostSceneWait wait in waits)
            {
                wait.Failed();
            }
        }

        private void HideUnobservedOnHost(ulong localPeerId)
        {
            if (!connectedLocally)
            {
                return;
            }

            foreach (EntityRecord record in serverManager.Entities.InSpawnOrder)
            {
                if (!serverManager.Objects.IsObserver(record.ObjectId, localPeerId))
                {
                    EntityBackend.HideOnHost(record.Representation);
                }
            }
        }

        private void HideIfUnobservedOnHost(EntityRecord record)
        {
            if (connectedLocally
                && session.State == ConnectionState.Started
                && Objects.LocalPeerId != 0
                && record.Server == serverManager.Entities
                && !serverManager.Objects.IsObserver(record.ObjectId, Objects.LocalPeerId))
            {
                EntityBackend.HideOnHost(record.Representation);
            }
        }

        private void FollowServerTickRate(ulong localPeerId)
        {
            if (connectedLocally)
            {
                return;
            }

            timeManager.UseTickRate(Objects.ServerTickRate);
            clock.Estimator.SetTickRate(Objects.ServerTickRate);
        }

        private void RestoreTickRateWhenStopped(ConnectionStateArgs args)
        {
            if (args.State == ConnectionState.Stopped)
            {
                timeManager.RestoreTickRate();
            }
        }

        private void EndHostVisibilityWhenStopped(ConnectionStateArgs args)
        {
            if (args.State != ConnectionState.Stopped || !connectedLocally)
            {
                return;
            }

            Objects.ForgetLoadedScenes();

            Entities.DespawnHiddenOnHost();
            foreach (EntityRecord record in serverManager.Entities.InSpawnOrder)
            {
                EntityBackend.ShowOnHost(record.Representation);
            }
        }

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

        private void RaiseObjectMismatch(ObjectMismatchArgs args)
        {
            Action<ObjectMismatchArgs> handlers = OnObjectMismatch;
            if (handlers == null)
            {
                log.Error($"Fomoxa stopped the client: object mismatch {args.Kind}, object {args.ObjectId}, prefab 0x{args.PrefabId:X8}, scene object 0x{args.SceneObjectId:X16}");
                return;
            }

            handlers(args);
        }

        private readonly struct HostSceneWait
        {
            public HostSceneWait(Action loaded, Action failed)
            {
                Loaded = loaded;
                Failed = failed;
            }

            public Action Loaded { get; }

            public Action Failed { get; }
        }

        private sealed class HostAwareSceneHost : ISceneHost
        {
            private readonly ClientManager owner;

            public HostAwareSceneHost(ClientManager owner)
            {
                this.owner = owner;
            }

            public bool TryLoad(uint sceneId, Action loaded, Action failed) => owner.LoadScene(sceneId, loaded, failed);

            public void Unload(uint sceneId, Action unloaded) => owner.UnloadScene(sceneId, unloaded);
        }
    }
}
