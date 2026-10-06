using System.Collections.Generic;
using System;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Prediction;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Simulation;
using Fomoxa.Networking.Timing;
using Fomoxa.Networking;
using UnityEngine.SceneManagement;
using UnityEngine;

namespace Fomoxa.Unity
{
    public sealed class ClientManager
    {
        private readonly TransportManager transportManager;
        private readonly ServerManager serverManager;
        private readonly PrefabRegistry prefabs;
        private readonly ClientSession session;
        private readonly ClientReconnector reconnector;
        private readonly Dictionary<ulong, NetworkObject> sceneObjects = new Dictionary<ulong, NetworkObject>();
        private readonly SceneRegistry sceneRegistry;
        private readonly Dictionary<uint, Scene> clientScenes = new Dictionary<uint, Scene>();
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
            PrefabRegistry prefabs,
            SceneRegistry sceneRegistry,
            RpcMessageIds rpcIds,
            ReconnectPolicy reconnectPolicy,
            TransportManager transportManager,
            ServerManager serverManager)
        {
            this.transportManager = transportManager;
            this.serverManager = serverManager;
            this.prefabs = prefabs;
            this.sceneRegistry = sceneRegistry;
            channels = protocol.Channels;
            this.stateProtocol = stateProtocol;
            this.timeManager = timeManager;
            RpcIds = rpcIds;
            Dispatcher = new MessageDispatcher(schema);
            session = new ClientSession(schema, sessionConfig, limits, Dispatcher, protocol);
            session.OnHandlerException += RaiseHandlerException;
            Entities = new ClientEntities(this, session, Dispatcher, channels, stateProtocol, transformProtocol, rpcIds, serverManager.Entities, new EntityBackend(this, prefabs), UnityNetworkLog.Instance);
            Prediction = new ClientPrediction(Entities, session, Dispatcher, inputProtocol, new PredictionBackend(this), UnityNetworkLog.Instance);
            session.OnClientConnectionState += PrepareSceneObjectsWhenStarted;
            session.OnClientConnectionState += EndHostVisibilityWhenStopped;
            reconnector = new ClientReconnector(session, reconnectPolicy);
            Objects = new ClientObjects(session, objectProtocol, Entities, sceneProtocol, new SceneHost(this));
            Entities.Attach(Objects);
            Objects.OnObjectMismatch += RaiseObjectMismatch;
            Objects.OnLocalPeerAssigned += HideUnobservedOnHost;
            Objects.OnLocalPeerAssigned += FollowServerTickRate;
            clock = new ClientClock(session, clockProtocol, clockSettings);
            session.OnClientConnectionState += RestoreTickRateWhenStopped;
            serverManager.OnSpawnedForHost += HideIfUnobservedOnHost;
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

        internal PhysicsWorlds Physics { get; set; }

        internal bool SimulatesPhysics
        {
            get => Prediction.SimulatesPhysics;
            set => Prediction.SimulatesPhysics = value;
        }

        internal ClientPrediction Prediction { get; }

        internal bool RecordsContacts => !connectedLocally && session.State == ConnectionState.Started;

        internal RpcMessageIds RpcIds { get; }

        internal Func<IReadOnlyList<NetworkObject>> FindSceneObjects { get; set; } = SceneObjects.InLoadedScenes;

        public void StartConnection(string address, ushort port)
        {
            if (session.State != ConnectionState.Stopped)
            {
                throw new InvalidOperationException("the client is already running");
            }

            if (serverManager.State == ServerState.Started)
            {
                Entities.ConnectedLocally = true;
                reconnector.StartWithoutRetry(serverManager.LocalListener.Connect(), MonotonicClock.Now);
                return;
            }

            Entities.ConnectedLocally = false;
            reconnector.Start(() => transportManager.CreateConnector(address, port), MonotonicClock.Now);
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

        internal void EndShared(NetworkObject shared) => Entities.EndShared(((INetworkEntity)shared).Record);

        internal void ForgetDestroyed(NetworkObject networkObject)
        {
            if (Entities.ForgetDestroyed(networkObject)
                && sceneObjects.TryGetValue(networkObject.SceneObjectId, out NetworkObject sceneObject)
                && sceneObject == networkObject)
            {
                sceneObjects.Remove(networkObject.SceneObjectId);
            }
        }

        private void PrepareSceneObjectsWhenStarted(ConnectionStateArgs args)
        {
            if (args.State != ConnectionState.Started || connectedLocally)
            {
                return;
            }

            sceneObjects.Clear();
            PrepareSceneObjects(FindSceneObjects());
            foreach (Scene scene in clientScenes.Values)
            {
                PrepareSceneObjects(scene);
            }
        }

        private void PrepareSceneObjects(Scene scene)
        {
            var found = new List<NetworkObject>();
            SceneObjects.AddFrom(scene, found);
            PrepareSceneObjects(found);
        }

        private void PrepareSceneObjects(IReadOnlyList<NetworkObject> found)
        {
            foreach (NetworkObject sceneObject in found)
            {
                if (sceneObject.IsSpawned || !sceneObjects.TryAdd(sceneObject.SceneObjectId, sceneObject))
                {
                    continue;
                }

                sceneObject.gameObject.SetActive(false);
            }
        }

        private bool LoadScene(uint sceneId, Action loaded, Action failed)
        {
            if (connectedLocally)
            {
                if (serverManager.TryGetNetworkScene(sceneId, out Scene _))
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

            if (!sceneRegistry.TryGet(sceneId, out ISceneLoader loader))
            {
                return false;
            }

            loader.Load(
                scene =>
                {
                    clientScenes[sceneId] = scene;
                    PrepareSceneObjects(scene);
                    loaded();
                },
                exception =>
                {
                    Debug.LogException(exception);
                    failed();
                });
            return true;
        }

        private void UnloadScene(uint sceneId, Action unloaded)
        {
            if (connectedLocally || !clientScenes.Remove(sceneId, out Scene scene) || !sceneRegistry.TryGet(sceneId, out ISceneLoader loader))
            {
                unloaded();
                return;
            }

            var leaving = new List<ulong>();
            foreach (KeyValuePair<ulong, NetworkObject> entry in sceneObjects)
            {
                if (entry.Value == null || entry.Value.gameObject.scene == scene)
                {
                    leaving.Add(entry.Key);
                }
            }

            foreach (ulong sceneObjectId in leaving)
            {
                sceneObjects.Remove(sceneObjectId);
            }

            loader.Unload(scene, unloaded);
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
                    ((NetworkObject)record.Representation).HideOnHost();
                }
            }
        }

        private void HideIfUnobservedOnHost(NetworkObject networkObject)
        {
            if (connectedLocally
                && session.State == ConnectionState.Started
                && Objects.LocalPeerId != 0
                && networkObject.Server == serverManager
                && !serverManager.Objects.IsObserver(networkObject.ObjectId, Objects.LocalPeerId))
            {
                networkObject.HideOnHost();
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
                ((NetworkObject)record.Representation).ShowOnHost();
            }
        }

        private bool TryGetObject(uint objectId, out NetworkObject networkObject)
        {
            if (Entities.TryGet(objectId, out EntityRecord record))
            {
                networkObject = (NetworkObject)record.Representation;
                return true;
            }

            networkObject = null;
            return false;
        }

        private SpawnResult PlaceSceneObject(in SpawnedObject spawnedObject, out NetworkObject instance)
        {
            if (!sceneObjects.TryGetValue(spawnedObject.SceneObjectId, out instance) || instance == null)
            {
                instance = null;
                return SpawnResult.UnknownSceneObject;
            }

            if (!PrefabHash.TryDescribeSceneObject(instance, out uint fingerprint, out string _) || fingerprint != spawnedObject.PrefabFingerprint)
            {
                return SpawnResult.IncompatibleSceneObject;
            }

            instance.transform.SetPositionAndRotation(spawnedObject.Position.ToUnity(), spawnedObject.Rotation.ToUnity());
            instance.transform.localScale = spawnedObject.Scale.ToUnity();
            instance.CollectBehaviours();
            instance.Fingerprint = fingerprint;
            instance.gameObject.SetActive(true);
            return SpawnResult.Spawned;
        }

        private void End(NetworkObject instance)
        {
            if (sceneObjects.TryGetValue(instance.SceneObjectId, out NetworkObject sceneObject) && sceneObject == instance)
            {
                instance.gameObject.SetActive(false);
                return;
            }

            if (prefabs.TryGet(instance.PrefabId, out PrefabEntry entry))
            {
                Release(entry, instance);
                return;
            }

            instance.DestroyGameObject();
        }

        private static NetworkObject Create(PrefabEntry entry, in SpawnedObject spawnedObject)
        {
            Vector3 position = spawnedObject.Position.ToUnity();
            Quaternion rotation = spawnedObject.Rotation.ToUnity();
            NetworkObject instance;
            if (entry.Create == null)
            {
                instance = UnityEngine.Object.Instantiate(entry.Prefab, position, rotation);
            }
            else
            {
                instance = entry.Create(position, rotation);
                if (instance == null)
                {
                    throw new InvalidOperationException($"the factory of prefab 0x{entry.PrefabId:X8} returned no NetworkObject");
                }

                if (instance.IsSpawned)
                {
                    throw new InvalidOperationException($"the factory of prefab 0x{entry.PrefabId:X8} returned {instance.name}, which is already spawned");
                }
            }

            try
            {
                instance.transform.localScale = spawnedObject.Scale.ToUnity();
                instance.SetPrefabId(entry.PrefabId);
                instance.CollectBehaviours();
            }
            catch
            {
                Release(entry, instance);
                throw;
            }

            return instance;
        }

        private static void Release(PrefabEntry entry, NetworkObject instance)
        {
            if (entry.Release != null)
            {
                entry.Release(instance);
                return;
            }

            instance.DestroyGameObject();
        }

        private void RaiseHandlerException(HandlerExceptionArgs args)
        {
            Action<HandlerExceptionArgs> handlers = OnHandlerException;
            if (handlers == null)
            {
                Debug.LogException(args.Exception);
                return;
            }

            handlers(args);
        }

        private void RaiseObjectMismatch(ObjectMismatchArgs args)
        {
            Action<ObjectMismatchArgs> handlers = OnObjectMismatch;
            if (handlers == null)
            {
                Debug.LogError($"Fomoxa stopped the client: object mismatch {args.Kind}, object {args.ObjectId}, prefab 0x{args.PrefabId:X8}, scene object 0x{args.SceneObjectId:X16}");
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

        private sealed class EntityBackend : IClientEntityBackend
        {
            private readonly ClientManager owner;
            private readonly PrefabRegistry prefabs;

            public EntityBackend(ClientManager owner, PrefabRegistry prefabs)
            {
                this.owner = owner;
                this.prefabs = prefabs;
            }

            public SpawnResult CheckPrefab(in SpawnedObject spawned)
            {
                if (!prefabs.TryGet(spawned.PrefabId, out PrefabEntry entry))
                {
                    return SpawnResult.UnknownPrefab;
                }

                return entry.Fingerprint == spawned.PrefabFingerprint ? SpawnResult.Spawned : SpawnResult.IncompatiblePrefab;
            }

            public INetworkEntity Create(in SpawnedObject spawned)
            {
                prefabs.TryGet(spawned.PrefabId, out PrefabEntry entry);
                NetworkObject instance = ClientManager.Create(entry, spawned);
                if (spawned.SceneId != 0 && owner.clientScenes.TryGetValue(spawned.SceneId, out Scene scene))
                {
                    SceneManager.MoveGameObjectToScene(instance.gameObject, scene);
                }

                return instance;
            }

            public SpawnResult PlaceSceneObject(in SpawnedObject spawned, out INetworkEntity entity)
            {
                SpawnResult placed = owner.PlaceSceneObject(spawned, out NetworkObject instance);
                entity = instance;
                return placed;
            }

            public void End(INetworkEntity entity) => owner.End((NetworkObject)entity);

            public void HideOnHost(INetworkEntity entity) => ((NetworkObject)entity).HideOnHost();

            public void ShowOnHost(INetworkEntity entity) => ((NetworkObject)entity).ShowOnHost();
        }

        private sealed class PredictionBackend : IClientPredictionBackend
        {
            private readonly ClientManager owner;

            public PredictionBackend(ClientManager owner)
            {
                this.owner = owner;
            }

            public void WorldsOf(INetworkEntity entity, List<IPhysicsSimulation> worlds)
            {
                PhysicsWorlds physics = owner.Physics;
                if (physics == null)
                {
                    return;
                }

                Scene scene = ((NetworkObject)entity).gameObject.scene;
                worlds.Add(physics.Of(scene));
                worlds.Add(physics.Of2D(scene));
            }

            public PhysicsHistory HistoryOf(IPhysicsSimulation world, int capacity) => owner.Physics.HistoryOf(world, capacity);

            public void PlaceProxy(INetworkEntity entity) => owner.Physics?.PlaceProxy((NetworkObject)entity);

            public void EndProxy(INetworkEntity entity) => owner.Physics?.EndProxy((NetworkObject)entity);

            public void ForgetDestroyedProxies() => owner.Physics?.ForgetDestroyedProxies();

            public void BeginCorrection(INetworkEntity entity)
            {
                foreach (NetworkBehaviour behaviour in ((NetworkObject)entity).Behaviours)
                {
                    (behaviour as NetworkTransform)?.BeginCorrection();
                }
            }

            public void EndCorrection(INetworkEntity entity)
            {
                foreach (NetworkBehaviour behaviour in ((NetworkObject)entity).Behaviours)
                {
                    (behaviour as NetworkTransform)?.EndCorrection();
                }
            }

            public IContactTracker TrackerOf(IPhysicsSimulation world)
            {
                switch (world)
                {
                    case UnityPhysicsWorld world3D:
                        return ContactTrackers.Of(world3D.PhysicsScene);
                    case UnityPhysicsWorld2D world2D:
                        return ContactTrackers.Of(world2D.PhysicsScene);
                    default:
                        return null;
                }
            }
        }

        private sealed class SceneHost : ISceneHost
        {
            private readonly ClientManager owner;

            public SceneHost(ClientManager owner)
            {
                this.owner = owner;
            }

            public bool TryLoad(uint sceneId, Action loaded, Action failed) => owner.LoadScene(sceneId, loaded, failed);

            public void Unload(uint sceneId, Action unloaded) => owner.UnloadScene(sceneId, unloaded);
        }

    }
}
