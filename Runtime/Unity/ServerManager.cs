using System.Collections.Generic;
using System;
using Fomoxa.Net.Transports;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Prediction;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Timing;
using Fomoxa.Networking.Transports;
using Fomoxa.Networking;
using UnityEngine.SceneManagement;
using UnityEngine;

namespace Fomoxa.Unity
{
    public sealed class ServerManager
    {
        private readonly TransportManager transportManager;
        private readonly PrefabRegistry prefabs;
        private readonly ServerSession session;
        private readonly MessageChannels channels;
        private readonly StateProtocol stateProtocol;
        private readonly SceneRegistry sceneRegistry;
        private ObserverRule observerRule;
        private readonly Dictionary<uint, LoadedScene> networkScenes = new Dictionary<uint, LoadedScene>();
        private readonly Dictionary<Scene, uint> sceneIdsByScene = new Dictionary<Scene, uint>();
        private readonly HashSet<uint> loadingScenes = new HashSet<uint>();
        private readonly HashSet<uint> removedWhileLoading = new HashSet<uint>();
        private readonly HashSet<Scene> unloadingScenes = new HashSet<Scene>();
        private readonly HashSet<uint> unloadingIds = new HashSet<uint>();
        private readonly HashSet<uint> loadAfterUnload = new HashSet<uint>();
        private int sceneEpoch;

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
            PrefabRegistry prefabs,
            SceneRegistry sceneRegistry,
            RpcMessageIds rpcIds,
            TransportManager transportManager)
        {
            this.sceneRegistry = sceneRegistry;
            this.transportManager = transportManager;
            this.prefabs = prefabs;
            channels = protocol.Channels;
            this.stateProtocol = stateProtocol;
            RpcIds = rpcIds;
            Dispatcher = new MessageDispatcher(schema);
            session = new ServerSession(schema, sessionConfig, limits, Dispatcher, protocol);
            session.OnHandlerException += RaiseHandlerException;
            Objects = new ServerObjects(session, objectProtocol, objectId => Entities.ReadSpawnData(objectId), sceneProtocol);
            Clock = new ServerClock(session, clockProtocol);
            Inputs = new ServerInputs(session, Objects, inputProtocol, Clock);
            Entities = new ServerEntities(this, session, Objects, Inputs, Dispatcher, channels, stateProtocol, inputProtocol, transformProtocol, rpcIds, new EntityBackend(this, prefabs), UnityNetworkLog.Instance);
            Entities.OnSpawned += record => OnSpawnedForHost?.Invoke((NetworkObject)record.Representation);
            Entities.OnUnspawning += EndHostShare;
            Scenes = new NetworkScenes(this, sceneRegistry);
            Objects.Scenes.OnSceneAdded += LoadNetworkScene;
            Objects.Scenes.OnSceneRemoved += UnloadNetworkScene;
            session.OnServerConnectionState += DespawnAllWhenStopped;
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

        public ObserverRule ObserverRule
        {
            get => observerRule;
            set
            {
                observerRule = value;
                Entities.ObserverRule = value != null ? value.CoreRule : null;
            }
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

        internal PhysicsWorlds Physics { get; set; }

        internal Func<IReadOnlyList<NetworkObject>> FindSceneObjects { get; set; } = SceneObjects.InLoadedScenes;

        internal event Action<NetworkObject> OnSpawnedForHost;

        internal event Action<uint> OnNetworkSceneReady;

        internal event Action<uint> OnNetworkSceneFailed;

        public ConnectionState PeerState(ulong peerId) => session.PeerState(peerId);

        public void StartConnection(ushort port)
        {
            if (session.State != ServerState.Stopped)
            {
                throw new InvalidOperationException("the server is already running");
            }

            IListenerTransport listener = transportManager.CreateListener(port, out LoopbackListener localListener, out ushort boundPort);
            LocalListener = localListener;
            Port = boundPort;
            session.Start(listener);
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

        internal bool IsLoadingNetworkScene(uint sceneId) => loadingScenes.Contains(sceneId);

        internal bool TryGetNetworkScene(uint sceneId, out Scene scene)
        {
            if (networkScenes.TryGetValue(sceneId, out LoadedScene loaded))
            {
                scene = loaded.Scene;
                return true;
            }

            scene = default;
            return false;
        }

        internal void Tick(TimeSpan now)
        {
            Entities.NextAnchorRound();
            session.Tick(now);
        }

        internal void Flush() => session.Flush();

        internal void ApplyInputs(uint tick, ulong hostPeerId) => Entities.ApplyInputs(tick, hostPeerId);

        internal void SendReconcileStates(uint tick) => Entities.SendReconcileStates(tick);

        internal void DespawnDestroyed(NetworkObject networkObject) => Entities.DespawnDestroyed(networkObject);

        private void SpawnSceneObjects()
        {
            Entities.ResetSceneObjects();
            var found = new List<NetworkObject>();
            foreach (NetworkObject sceneObject in FindSceneObjects())
            {
                if (!unloadingScenes.Contains(sceneObject.gameObject.scene))
                {
                    found.Add(sceneObject);
                }
            }

            Entities.SpawnSceneObjects(found);
        }

        private uint SceneIdOf(Scene scene) =>
            scene.IsValid() && sceneIdsByScene.TryGetValue(scene, out uint sceneId) ? sceneId : 0;

        private void LoadNetworkScene(uint sceneId)
        {
            if (loadingScenes.Contains(sceneId))
            {
                removedWhileLoading.Remove(sceneId);
                return;
            }

            if (unloadingIds.Contains(sceneId))
            {
                loadAfterUnload.Add(sceneId);
                return;
            }

            if (networkScenes.ContainsKey(sceneId) || !sceneRegistry.TryGet(sceneId, out ISceneLoader loader))
            {
                return;
            }

            loadingScenes.Add(sceneId);
            int epoch = sceneEpoch;
            loader.Load(
                scene => FinishNetworkScene(sceneId, scene, loader, epoch),
                exception => FailNetworkScene(sceneId, exception, epoch));
        }

        private void FinishNetworkScene(uint sceneId, Scene scene, ISceneLoader loader, int epoch)
        {
            if (epoch != sceneEpoch)
            {
                loader.Unload(scene, () => { });
                return;
            }

            loadingScenes.Remove(sceneId);
            if (removedWhileLoading.Remove(sceneId) || session.State != ServerState.Started)
            {
                loader.Unload(scene, () => { });
                return;
            }

            networkScenes.Add(sceneId, new LoadedScene(scene, loader));
            sceneIdsByScene[scene] = sceneId;
            var found = new List<NetworkObject>();
            SceneObjects.AddFrom(scene, found);
            Entities.SpawnSceneObjects(found);
            OnNetworkSceneReady?.Invoke(sceneId);
            Scenes.RaiseLoaded(sceneId);
        }

        private void FailNetworkScene(uint sceneId, Exception exception, int epoch)
        {
            if (epoch != sceneEpoch)
            {
                return;
            }

            loadingScenes.Remove(sceneId);
            removedWhileLoading.Remove(sceneId);
            Debug.LogException(exception);
            OnNetworkSceneFailed?.Invoke(sceneId);
            Scenes.RaiseLoadFailed(sceneId, exception);
        }

        private void UnloadNetworkScene(uint sceneId)
        {
            if (loadAfterUnload.Remove(sceneId))
            {
                return;
            }

            if (loadingScenes.Contains(sceneId))
            {
                removedWhileLoading.Add(sceneId);
                return;
            }

            if (networkScenes.Remove(sceneId, out LoadedScene loaded))
            {
                ReleaseNetworkScene(sceneId, loaded);
            }
        }

        private void ReleaseNetworkScene(uint sceneId, LoadedScene loaded)
        {
            Scene scene = loaded.Scene;
            sceneIdsByScene.Remove(scene);
            Entities.ForgetSceneObjects(entity => (NetworkObject)entity == null || ((NetworkObject)entity).gameObject.scene == scene);
            unloadingScenes.Add(scene);
            unloadingIds.Add(sceneId);
            loaded.Loader.Unload(scene, () => FinishUnload(sceneId, scene));
        }

        private void FinishUnload(uint sceneId, Scene scene)
        {
            unloadingScenes.Remove(scene);
            unloadingIds.Remove(sceneId);
            if (loadAfterUnload.Remove(sceneId))
            {
                LoadNetworkScene(sceneId);
            }
        }

        private void DespawnAllWhenStopped(ServerConnectionStateArgs args)
        {
            if (args.State != ServerState.Stopped)
            {
                return;
            }

            sceneEpoch++;
            loadingScenes.Clear();
            removedWhileLoading.Clear();
            loadAfterUnload.Clear();
            var loaded = new List<KeyValuePair<uint, LoadedScene>>(networkScenes);
            networkScenes.Clear();
            foreach (KeyValuePair<uint, LoadedScene> scene in loaded)
            {
                ReleaseNetworkScene(scene.Key, scene.Value);
            }
        }

        internal void SyncStates() => Entities.SyncStates();

        internal void SyncTransforms(uint tick) => Entities.SyncTransforms(tick);

        private void EndHostShare(EntityRecord record)
        {
            var networkObject = (NetworkObject)record.Representation;
            networkObject.Client?.EndShared(networkObject);
            networkObject.RestoreRenderers();
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

        private sealed class EntityBackend : IServerEntityBackend
        {
            private readonly ServerManager server;
            private readonly PrefabRegistry prefabs;

            public EntityBackend(ServerManager server, PrefabRegistry prefabs)
            {
                this.server = server;
                this.prefabs = prefabs;
            }

            public string NameOf(INetworkEntity entity) => ((NetworkObject)entity).name;

            public void ValidateSpawn(INetworkEntity entity)
            {
                var networkObject = (NetworkObject)entity;
                if (!networkObject.gameObject.scene.IsValid())
                {
                    throw new ArgumentException($"{networkObject.name} is a prefab asset; spawn an instance of it", nameof(networkObject));
                }
            }

            public uint FingerprintOf(INetworkEntity entity, bool isSceneObject)
            {
                var networkObject = (NetworkObject)entity;
                if (isSceneObject)
                {
                    if (!PrefabHash.TryDescribeSceneObject(networkObject, out uint fingerprint, out string error))
                    {
                        throw new ArgumentException($"{networkObject.name}: {error}", nameof(networkObject));
                    }

                    return fingerprint;
                }

                if (prefabs.TryGet(networkObject.PrefabId, out PrefabEntry entry))
                {
                    return entry.Fingerprint;
                }

                throw new ArgumentException($"prefab id 0x{networkObject.PrefabId:X8} of {networkObject.name} is not registered", nameof(networkObject));
            }

            public uint SceneIdOf(INetworkEntity entity) => server.SceneIdOf(((NetworkObject)entity).gameObject.scene);

            public void PrepareSpawn(INetworkEntity entity)
            {
                ((NetworkObject)entity).CollectBehaviours();
            }

            public void Activate(INetworkEntity entity) => ((NetworkObject)entity).gameObject.SetActive(true);

            public void End(INetworkEntity entity, bool isSceneObject)
            {
                var networkObject = (NetworkObject)entity;
                if (isSceneObject)
                {
                    networkObject.gameObject.SetActive(false);
                    return;
                }

                networkObject.DestroyGameObject();
            }
        }

        private readonly struct LoadedScene
        {
            public LoadedScene(Scene scene, ISceneLoader loader)
            {
                Scene = scene;
                Loader = loader;
            }

            public Scene Scene { get; }

            public ISceneLoader Loader { get; }
        }

    }
}
