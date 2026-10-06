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
        private readonly SceneHost sceneHost;
        private ObserverRule observerRule;

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
            sceneHost = new SceneHost(sceneRegistry);
            SceneContent = new ServerSceneContent(session, Objects.Scenes, Entities, sceneHost, UnityNetworkLog.Instance);
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

        internal ServerSceneContent SceneContent { get; }

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

        internal bool IsLoadingNetworkScene(uint sceneId) => SceneContent.IsLoading(sceneId);

        internal bool TryGetNetworkScene(uint sceneId, out Scene scene) => sceneHost.TryGetScene(sceneId, out scene);

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
                if (!sceneHost.IsUnloading(sceneObject.gameObject.scene))
                {
                    found.Add(sceneObject);
                }
            }

            Entities.SpawnSceneObjects(found);
        }

        private uint SceneIdOf(Scene scene) => sceneHost.SceneIdOf(scene);

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

        private sealed class SceneHost : IServerSceneHost
        {
            private readonly SceneRegistry registry;
            private readonly Dictionary<uint, LoadedScene> scenes = new Dictionary<uint, LoadedScene>();
            private readonly Dictionary<Scene, uint> idsByScene = new Dictionary<Scene, uint>();
            private readonly HashSet<Scene> unloadingScenes = new HashSet<Scene>();

            public SceneHost(SceneRegistry registry)
            {
                this.registry = registry;
            }

            public bool TryLoad(uint sceneId, Func<bool> accept, Action loaded, Action<Exception> failed)
            {
                if (!registry.TryGet(sceneId, out ISceneLoader loader))
                {
                    return false;
                }

                loader.Load(scene => Arrive(sceneId, scene, loader, accept, loaded), failed);
                return true;
            }

            public void Unload(uint sceneId, Action unloaded)
            {
                if (!scenes.Remove(sceneId, out LoadedScene loaded))
                {
                    unloaded();
                    return;
                }

                Scene scene = loaded.Scene;
                idsByScene.Remove(scene);
                unloadingScenes.Add(scene);
                loaded.Loader.Unload(scene, () =>
                {
                    unloadingScenes.Remove(scene);
                    unloaded();
                });
            }

            public void SceneObjectsOf(uint sceneId, List<INetworkEntity> found)
            {
                if (!scenes.TryGetValue(sceneId, out LoadedScene loaded))
                {
                    return;
                }

                var objects = new List<NetworkObject>();
                SceneObjects.AddFrom(loaded.Scene, objects);
                found.AddRange(objects);
            }

            public bool Holds(uint sceneId, INetworkEntity entity)
            {
                var networkObject = (NetworkObject)entity;
                return networkObject == null || (scenes.TryGetValue(sceneId, out LoadedScene loaded) && networkObject.gameObject.scene == loaded.Scene);
            }

            public bool TryGetScene(uint sceneId, out Scene scene)
            {
                if (scenes.TryGetValue(sceneId, out LoadedScene loaded))
                {
                    scene = loaded.Scene;
                    return true;
                }

                scene = default;
                return false;
            }

            public uint SceneIdOf(Scene scene) =>
                scene.IsValid() && idsByScene.TryGetValue(scene, out uint sceneId) ? sceneId : 0;

            public bool IsUnloading(Scene scene) => unloadingScenes.Contains(scene);

            private void Arrive(uint sceneId, Scene scene, ISceneLoader loader, Func<bool> accept, Action loaded)
            {
                if (!accept())
                {
                    loader.Unload(scene, () => { });
                    return;
                }

                scenes.Add(sceneId, new LoadedScene(scene, loader));
                idsByScene[scene] = sceneId;
                loaded();
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
    }
}
