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
        private readonly List<ReadOnlyMemory<byte>> spawnStates = new List<ReadOnlyMemory<byte>>();
        private readonly MessageChannels channels;
        private readonly StateProtocol stateProtocol;
        private readonly TransformProtocol transformProtocol;
        private readonly TransformUpdate transformUpdate = new TransformUpdate();
        private readonly TransformSettle transformSettle = new TransformSettle();
        private readonly List<ObserverAddedArgs> enteredObservers = new List<ObserverAddedArgs>();
        private readonly Dictionary<ulong, AnchorPositions> anchors = new Dictionary<ulong, AnchorPositions>();
        private int anchorGeneration = 1;
        private readonly SceneRegistry sceneRegistry;
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
            this.transformProtocol = transformProtocol;
            RpcIds = rpcIds;
            Dispatcher = new MessageDispatcher(schema);
            session = new ServerSession(schema, sessionConfig, limits, Dispatcher, protocol);
            session.OnHandlerException += RaiseHandlerException;
            Objects = new ServerObjects(session, objectProtocol, ReadSpawnData, sceneProtocol);
            Clock = new ServerClock(session, clockProtocol);
            Inputs = new ServerInputs(session, Objects, inputProtocol, Clock);
            Entities = new ServerEntities(this, session, Objects, Inputs, Dispatcher, channels, stateProtocol, inputProtocol, rpcIds, new EntityBackend(this, prefabs), UnityNetworkLog.Instance);
            Entities.ObserverRule = DecideByRule;
            Entities.OnSpawned += record => OnSpawnedForHost?.Invoke((NetworkObject)record.Representation);
            Entities.OnUnspawning += EndHostShare;
            Entities.OnFirstOwned += RebuildForFirstAnchor;
            Spawned = new RepresentationView(Entities.Spawned);
            Scenes = new NetworkScenes(this, sceneRegistry);
            Objects.Scenes.OnSceneAdded += LoadNetworkScene;
            Objects.Scenes.OnSceneRemoved += UnloadNetworkScene;
            Objects.OnObserverAdded += enteredObservers.Add;
            session.OnRemoteConnectionState += ForgetLeavingPeer;
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

        public IReadOnlyDictionary<uint, NetworkObject> Spawned { get; }

        public ObserverRule ObserverRule { get; set; }

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

            anchorGeneration++;
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

            anchorGeneration++;
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

            anchorGeneration++;
            return Entities.RebuildObservers(networkObject);
        }

        public void RebuildObserversOfPeer(ulong peerId)
        {
            anchorGeneration++;
            Objects.RebuildObserversOfPeer(peerId);
        }

        public void RebuildObservers()
        {
            anchorGeneration++;
            Objects.RebuildObservers();
        }

        internal int BroadcastToObject(uint messageId, uint objectId, byte behaviourIndex, ReadOnlySpan<byte> body) =>
            Objects.SendToObservers(messageId, objectId, behaviourIndex, body);

        internal SendResult SendToObserver(ulong peerId, uint messageId, uint objectId, byte behaviourIndex, ReadOnlySpan<byte> body) =>
            Objects.SendToObserver(peerId, messageId, objectId, behaviourIndex, body);

        internal void RebuildObserversRound()
        {
            anchorGeneration++;
            Objects.RebuildObserversRound();
        }

        internal IReadOnlyList<Vector3> AnchorsOf(ulong peerId)
        {
            IReadOnlyList<EntityRecord> owned = Entities.OwnedBy(peerId);
            if (owned.Count == 0)
            {
                return Array.Empty<Vector3>();
            }

            if (!anchors.TryGetValue(peerId, out AnchorPositions cached))
            {
                cached = new AnchorPositions();
                anchors.Add(peerId, cached);
            }

            if (cached.Generation != anchorGeneration)
            {
                cached.Generation = anchorGeneration;
                cached.Positions.Clear();
                foreach (EntityRecord record in owned)
                {
                    cached.Positions.Add(((NetworkObject)record.Representation).transform.position);
                }
            }

            return cached.Positions;
        }

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
            anchorGeneration++;
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

            anchors.Clear();
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

        private SpawnData ReadSpawnData(uint objectId)
        {
            EntityRecord record = Entities.Spawning;
            if (record == null && !Entities.TryGet(objectId, out record))
            {
                throw new InvalidOperationException($"object {objectId} has no NetworkObject on the server");
            }

            var networkObject = (NetworkObject)record.Representation;

            spawnStates.Clear();
            foreach (NetworkBehaviour behaviour in networkObject.Behaviours)
            {
                spawnStates.Add(behaviour.StateSlot?.Sent ?? ReadOnlyMemory<byte>.Empty);
            }

            Transform transform = networkObject.transform;
            return new SpawnData(
                record.Fingerprint,
                transform.position.ToNumerics(),
                transform.rotation.ToNumerics(),
                transform.localScale.ToNumerics(),
                spawnStates);
        }

        internal void SyncStates() => Entities.SyncStates();

        internal void SyncTransforms(uint tick)
        {
            foreach (ObserverAddedArgs entered in enteredObservers)
            {
                if (!Entities.TryGet(entered.ObjectId, out EntityRecord enteredRecord) || !Objects.IsObserver(entered.ObjectId, entered.PeerId))
                {
                    continue;
                }

                var networkObject = (NetworkObject)enteredRecord.Representation;

                IReadOnlyList<NetworkBehaviour> behaviours = networkObject.Behaviours;
                for (int index = 0; index < behaviours.Count; index++)
                {
                    if (behaviours[index] is NetworkTransform networkTransform && !networkTransform.SettlePending)
                    {
                        ReadOnlySpan<byte> settle = EncodeSettle(networkTransform, tick, networkTransform.SelectedMask);
                        session.SendToObject(entered.PeerId, transformProtocol.SettleCodec.MessageId, networkObject.ObjectId, (byte)index, settle);
                    }
                }
            }

            enteredObservers.Clear();
            foreach (EntityRecord record in Entities.InSpawnOrder)
            {
                var networkObject = (NetworkObject)record.Representation;
                IReadOnlyList<NetworkBehaviour> behaviours = networkObject.Behaviours;
                for (int index = 0; index < behaviours.Count; index++)
                {
                    if (!(behaviours[index] is NetworkTransform networkTransform))
                    {
                        continue;
                    }

                    if (networkTransform.SettlePending)
                    {
                        networkTransform.SettlePending = false;
                        networkTransform.MarkSettled();
                        Objects.SendToObservers(transformProtocol.SettleCodec.MessageId, networkObject.ObjectId, (byte)index, EncodeSettle(networkTransform, tick, networkTransform.SelectedMask));
                        continue;
                    }

                    TransformSend send = networkTransform.Sample(out byte mask);
                    if (send == TransformSend.Update)
                    {
                        Transform target = networkTransform.transform;
                        transformUpdate.Tick = tick;
                        transformUpdate.Mask = mask;
                        transformUpdate.Generation = networkTransform.Generation;
                        SpawnTransform.PackSelected(mask, target.localPosition.ToNumerics(), target.localRotation.ToNumerics(), target.localScale.ToNumerics(), transformUpdate.Values);
                        Objects.SendToObservers(transformProtocol.UpdateCodec.MessageId, networkObject.ObjectId, (byte)index, transformProtocol.UpdateCodec.Encode(transformUpdate).Span);
                    }
                    else if (send == TransformSend.Settle)
                    {
                        Objects.SendToObservers(transformProtocol.SettleCodec.MessageId, networkObject.ObjectId, (byte)index, EncodeSettle(networkTransform, tick, mask));
                    }
                }
            }
        }

        private ReadOnlySpan<byte> EncodeSettle(NetworkTransform networkTransform, uint tick, byte mask)
        {
            Transform target = networkTransform.transform;
            transformSettle.Tick = tick;
            transformSettle.Mask = mask;
            transformSettle.Generation = networkTransform.Generation;
            SpawnTransform.PackSelected(mask, target.localPosition.ToNumerics(), target.localRotation.ToNumerics(), target.localScale.ToNumerics(), transformSettle.Values);
            return transformProtocol.SettleCodec.Encode(transformSettle).Span;
        }

        private void ForgetLeavingPeer(ConnectionStateArgs args)
        {
            if (args.State == ConnectionState.Stopped)
            {
                anchors.Remove(args.PeerId);
            }
        }

        private void RebuildForFirstAnchor(ulong peerId)
        {
            ObserverRule rule = ObserverRule;
            if (rule != null && rule.RebuildsOnFirstAnchor && session.PeerState(peerId) == ConnectionState.Started)
            {
                Objects.RebuildObserversOfPeer(peerId);
            }
        }

        private bool DecideByRule(EntityRecord record, ulong peerId)
        {
            ObserverRule rule = ObserverRule;
            return rule == null || rule.Decide(this, (NetworkObject)record.Representation, peerId);
        }

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
                var networkObject = (NetworkObject)entity;
                networkObject.CollectBehaviours();
                foreach (NetworkBehaviour behaviour in networkObject.Behaviours)
                {
                    (behaviour as NetworkTransform)?.CaptureSpawn();
                }
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

        private sealed class AnchorPositions
        {
            public readonly List<Vector3> Positions = new List<Vector3>();

            public int Generation;
        }

    }
}
