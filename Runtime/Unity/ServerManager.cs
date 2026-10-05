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
        private readonly Dictionary<uint, NetworkObject> spawned = new Dictionary<uint, NetworkObject>();
        private readonly LinkedList<NetworkObject> spawnOrder = new LinkedList<NetworkObject>();
        private readonly HashSet<NetworkObject> sceneObjects = new HashSet<NetworkObject>();
        private readonly Dictionary<uint, Type> rpcTypes = new Dictionary<uint, Type>();
        private readonly HashSet<uint> stateIds = new HashSet<uint>();
        private readonly List<ReadOnlyMemory<byte>> spawnStates = new List<ReadOnlyMemory<byte>>();
        private readonly MessageChannels channels;
        private readonly StateProtocol stateProtocol;
        private readonly StateDelta stateDelta = new StateDelta();
        private readonly TransformProtocol transformProtocol;
        private readonly TransformUpdate transformUpdate = new TransformUpdate();
        private readonly TransformSettle transformSettle = new TransformSettle();
        private readonly List<ObserverAddedArgs> enteredObservers = new List<ObserverAddedArgs>();
        private readonly Dictionary<ulong, List<NetworkObject>> ownedObjects = new Dictionary<ulong, List<NetworkObject>>();
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
        private NetworkObject spawning;
        private readonly List<NetworkObject> applying = new List<NetworkObject>();
        private readonly List<ReconcileSend> reconciling = new List<ReconcileSend>();
        private readonly InputProtocol inputProtocol;
        private readonly ReconcileState reconcileState = new ReconcileState();

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
            Dispatcher.RegisterObject(stateProtocol.ResyncCodec.MessageId, Resync);
            Objects = new ServerObjects(session, objectProtocol, ReadSpawnData, sceneProtocol);
            Clock = new ServerClock(session, clockProtocol);
            this.inputProtocol = inputProtocol;
            Inputs = new ServerInputs(session, Objects, inputProtocol, Clock);
            Inputs.OnInputRejected += LogRejectedInput;
            Scenes = new NetworkScenes(this, sceneRegistry);
            Objects.Scenes.OnSceneAdded += LoadNetworkScene;
            Objects.Scenes.OnSceneRemoved += UnloadNetworkScene;
            Objects.OnObserverAdded += enteredObservers.Add;
            Objects.Observes = DecideObserver;
            Objects.OnOwnerChanged += RaiseOwnerChanged;
            session.OnRemoteConnectionState += ForgetLeavingPeer;
            Objects.DespawnWithOwner = objectId => !spawned.TryGetValue(objectId, out NetworkObject networkObject) || networkObject.DespawnWithOwner;
            Objects.Despawner = DespawnForLeavingOwner;
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

        public IReadOnlyDictionary<uint, NetworkObject> Spawned => spawned;

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

        internal InputRules InputRules { get; set; } = new InputRules();

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

        public void Spawn(NetworkObject networkObject, ulong ownerId = 0)
        {
            if (networkObject == null)
            {
                throw new ArgumentNullException(nameof(networkObject));
            }

            if (session.State != ServerState.Started)
            {
                throw new InvalidOperationException("the server is not started");
            }

            if (networkObject.IsSpawned)
            {
                throw new ArgumentException($"{networkObject.name} is already spawned", nameof(networkObject));
            }

            if (!networkObject.gameObject.scene.IsValid())
            {
                throw new ArgumentException($"{networkObject.name} is a prefab asset; spawn an instance of it", nameof(networkObject));
            }

            bool isSceneObject = sceneObjects.Contains(networkObject);
            uint fingerprint;
            if (isSceneObject)
            {
                if (!PrefabHash.TryDescribeSceneObject(networkObject, out fingerprint, out string error))
                {
                    throw new ArgumentException($"{networkObject.name}: {error}", nameof(networkObject));
                }
            }
            else if (prefabs.TryGet(networkObject.PrefabId, out PrefabEntry entry))
            {
                fingerprint = entry.Fingerprint;
            }
            else
            {
                throw new ArgumentException($"prefab id 0x{networkObject.PrefabId:X8} of {networkObject.name} is not registered", nameof(networkObject));
            }

            networkObject.CollectBehaviours();
            networkObject.Register(RpcIds, channels, stateProtocol, InputRules);
            RouteRpcs(networkObject);
            RouteStates(networkObject);
            networkObject.CaptureStates();
            foreach (NetworkBehaviour behaviour in networkObject.Behaviours)
            {
                (behaviour as NetworkTransform)?.CaptureSpawn();
            }
            networkObject.Fingerprint = fingerprint;
            uint objectId;
            uint sceneId = SceneIdOf(networkObject.gameObject.scene);
            anchorGeneration++;
            spawning = networkObject;
            try
            {
                objectId = isSceneObject
                    ? Objects.SpawnScene(networkObject.SceneObjectId, ownerId, sceneId)
                    : Objects.Spawn(networkObject.PrefabId, ownerId, sceneId);
            }
            finally
            {
                spawning = null;
            }

            if (isSceneObject)
            {
                networkObject.gameObject.SetActive(true);
            }

            networkObject.AttachServer(this, objectId);
            spawned.Add(objectId, networkObject);
            networkObject.SpawnOrderNode = spawnOrder.AddLast(networkObject);
            bool firstAnchor = AddOwned(ownerId, networkObject);
            try
            {
                networkObject.StartServer();
            }
            finally
            {
                OnSpawnedForHost?.Invoke(networkObject);
                if (firstAnchor)
                {
                    RebuildForFirstAnchor(ownerId);
                }
            }
        }

        public bool Despawn(NetworkObject networkObject)
        {
            if (networkObject == null)
            {
                throw new ArgumentNullException(nameof(networkObject));
            }

            return Unspawn(networkObject, true);
        }

        public bool ChangeOwner(NetworkObject networkObject, ulong ownerId)
        {
            if (networkObject == null)
            {
                throw new ArgumentNullException(nameof(networkObject));
            }

            if (networkObject.Server != this)
            {
                return false;
            }

            anchorGeneration++;
            ulong previousOwnerId = networkObject.OwnerId;
            if (!Objects.ChangeOwner(networkObject.ObjectId, ownerId))
            {
                return false;
            }

            if (previousOwnerId != ownerId && ownedObjects.TryGetValue(ownerId, out List<NetworkObject> owned) && owned.Count == 1)
            {
                RebuildForFirstAnchor(ownerId);
            }

            return true;
        }

        public bool IsObserver(NetworkObject networkObject, ulong peerId)
        {
            if (networkObject == null)
            {
                throw new ArgumentNullException(nameof(networkObject));
            }

            return networkObject.Server == this && Objects.IsObserver(networkObject.ObjectId, peerId);
        }

        public bool RebuildObservers(NetworkObject networkObject)
        {
            if (networkObject == null)
            {
                throw new ArgumentNullException(nameof(networkObject));
            }

            anchorGeneration++;
            return networkObject.Server == this && Objects.RebuildObservers(networkObject.ObjectId);
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
            if (!ownedObjects.TryGetValue(peerId, out List<NetworkObject> owned) || owned.Count == 0)
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
                foreach (NetworkObject networkObject in owned)
                {
                    cached.Positions.Add(networkObject.transform.position);
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

        internal void ApplyInputs(uint tick, ulong hostPeerId)
        {
            applying.Clear();
            foreach (NetworkObject networkObject in spawnOrder)
            {
                if (networkObject.HasInput)
                {
                    applying.Add(networkObject);
                }
            }

            foreach (NetworkObject networkObject in applying)
            {
                if (networkObject.Server != this || !Objects.TryGet(networkObject.ObjectId, out ObjectRow row) || row.OwnerId == 0)
                {
                    continue;
                }

                IReadOnlyList<NetworkBehaviour> behaviours = networkObject.Behaviours;
                for (int index = 0; index < behaviours.Count; index++)
                {
                    InputSlot slot = behaviours[index].InputSlot;
                    if (slot == null || networkObject.Server != this)
                    {
                        continue;
                    }

                    try
                    {
                        if (hostPeerId != 0 && row.OwnerId == hostPeerId)
                        {
                            slot.GatherAndApply(new InputContext(tick, false));
                        }
                        else if (Inputs.TakeInput(networkObject.ObjectId, (byte)index, tick, out ReadOnlyMemory<byte> input, out bool repeated, out uint repeatedTicks)
                            && ApplyInput(slot, input, new InputContext(tick, repeated, false, repeatedTicks), row.OwnerId)
                            && slot.Reconcile != null
                            && tick % (uint)InputRules.ReconcileInterval == 0)
                        {
                            reconciling.Add(new ReconcileSend(networkObject, (byte)index, row.OwnerId));
                        }
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                }
            }

            applying.Clear();
        }

        internal void SendReconcileStates(uint tick)
        {
            foreach (ReconcileSend send in reconciling)
            {
                NetworkObject networkObject = send.NetworkObject;
                if (networkObject == null || networkObject.Server != this)
                {
                    continue;
                }

                try
                {
                    SendReconcileState(networkObject.Behaviours[send.BehaviourIndex].InputSlot, send.OwnerId, networkObject.ObjectId, send.BehaviourIndex, tick);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            reconciling.Clear();
        }

        internal void DespawnDestroyed(NetworkObject networkObject)
        {
            Unspawn(networkObject, false);
            sceneObjects.Remove(networkObject);
        }

        private bool Unspawn(NetworkObject networkObject, bool destroy)
        {
            uint objectId = networkObject.ObjectId;
            if (networkObject.Server != this || !spawned.Remove(objectId))
            {
                return false;
            }

            spawnOrder.Remove(networkObject.SpawnOrderNode);
            networkObject.SpawnOrderNode = null;
            RemoveOwned(Objects.TryGet(objectId, out ObjectRow row) ? row.OwnerId : 0, networkObject);
            try
            {
                networkObject.Client?.EndShared(networkObject);
                networkObject.RestoreRenderers();
                networkObject.StopServer();
            }
            finally
            {
                if (session.State == ServerState.Started)
                {
                    Objects.Despawn(objectId);
                }

                networkObject.DetachServer();
                if (destroy)
                {
                    End(networkObject);
                }
            }

            return true;
        }

        private void End(NetworkObject networkObject)
        {
            if (sceneObjects.Contains(networkObject))
            {
                networkObject.gameObject.SetActive(false);
                return;
            }

            networkObject.DestroyGameObject();
        }

        private void SpawnSceneObjects()
        {
            sceneObjects.Clear();
            var found = new List<NetworkObject>();
            foreach (NetworkObject sceneObject in FindSceneObjects())
            {
                if (!unloadingScenes.Contains(sceneObject.gameObject.scene))
                {
                    found.Add(sceneObject);
                }
            }

            SpawnFound(found);
        }

        private void SpawnFound(IReadOnlyList<NetworkObject> found)
        {
            foreach (NetworkObject sceneObject in found)
            {
                if (!sceneObject.IsSpawned)
                {
                    sceneObjects.Add(sceneObject);
                }
            }

            foreach (NetworkObject sceneObject in found)
            {
                if (sceneObject.IsSpawned || !sceneObjects.Contains(sceneObject))
                {
                    continue;
                }

                try
                {
                    Spawn(sceneObject);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
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
            SpawnFound(found);
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
            sceneObjects.RemoveWhere(sceneObject => sceneObject == null || sceneObject.gameObject.scene == scene);
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

            while (spawnOrder.Last != null)
            {
                try
                {
                    Unspawn(spawnOrder.Last.Value, true);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            sceneObjects.Clear();
            ownedObjects.Clear();
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
            NetworkObject networkObject = spawning;
            if (networkObject == null && !spawned.TryGetValue(objectId, out networkObject))
            {
                throw new InvalidOperationException($"object {objectId} has no NetworkObject on the server");
            }

            spawnStates.Clear();
            foreach (NetworkBehaviour behaviour in networkObject.Behaviours)
            {
                spawnStates.Add(behaviour.StateSlot?.Sent ?? ReadOnlyMemory<byte>.Empty);
            }

            Transform transform = networkObject.transform;
            return new SpawnData(
                networkObject.Fingerprint,
                transform.position.ToNumerics(),
                transform.rotation.ToNumerics(),
                transform.localScale.ToNumerics(),
                spawnStates);
        }

        private void RouteRpcs(NetworkObject networkObject)
        {
            foreach (NetworkBehaviour behaviour in networkObject.Behaviours)
            {
                Type type = behaviour.GetType();
                foreach (uint messageId in behaviour.ServerRpcIds)
                {
                    if (stateIds.Contains(messageId))
                    {
                        throw new HandlerRegistrationException($"message id 0x{messageId:X8} is already a state model; {type.FullName} cannot use it as a server RPC");
                    }

                    if (rpcTypes.TryGetValue(messageId, out Type existing))
                    {
                        if (existing != type)
                        {
                            throw new HandlerRegistrationException($"message id 0x{messageId:X8} is already a server RPC of {existing.FullName}; {type.FullName} cannot use it");
                        }

                        continue;
                    }

                    uint routed = messageId;
                    Dispatcher.RegisterObject(routed, (peerId, objectId, behaviourIndex, body) => DeliverRpc(routed, peerId, objectId, behaviourIndex, body));
                    rpcTypes.Add(routed, type);
                }
            }
        }

        internal void SyncStates()
        {
            foreach (NetworkObject networkObject in spawnOrder)
            {
                IReadOnlyList<NetworkBehaviour> behaviours = networkObject.Behaviours;
                for (int index = 0; index < behaviours.Count; index++)
                {
                    StateSlot slot = behaviours[index].StateSlot;
                    if (slot == null)
                    {
                        continue;
                    }

                    behaviours[index].PrepareState();
                    if (!slot.Capture())
                    {
                        continue;
                    }

                    uint messageId = slot.MessageId;
                    ReadOnlySpan<byte> body = slot.Sent.Span;
                    if (slot.HasDelta)
                    {
                        stateDelta.Data = slot.Delta;
                        ReadOnlySpan<byte> delta = stateProtocol.DeltaCodec.Encode(stateDelta).Span;
                        if (delta.Length < body.Length)
                        {
                            messageId = stateProtocol.DeltaCodec.MessageId;
                            body = delta;
                        }
                    }

                    Objects.SendToObservers(messageId, networkObject.ObjectId, (byte)index, body);
                }
            }
        }

        internal void SyncTransforms(uint tick)
        {
            foreach (ObserverAddedArgs entered in enteredObservers)
            {
                if (!spawned.TryGetValue(entered.ObjectId, out NetworkObject networkObject) || !Objects.IsObserver(entered.ObjectId, entered.PeerId))
                {
                    continue;
                }

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
            foreach (NetworkObject networkObject in spawnOrder)
            {
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

        private bool AddOwned(ulong ownerId, NetworkObject networkObject)
        {
            if (ownerId == 0)
            {
                return false;
            }

            if (!ownedObjects.TryGetValue(ownerId, out List<NetworkObject> owned))
            {
                owned = new List<NetworkObject>();
                ownedObjects.Add(ownerId, owned);
            }

            owned.Add(networkObject);
            return owned.Count == 1;
        }

        private void RemoveOwned(ulong ownerId, NetworkObject networkObject)
        {
            if (ownerId != 0 && ownedObjects.TryGetValue(ownerId, out List<NetworkObject> owned))
            {
                owned.Remove(networkObject);
            }
        }

        private void ForgetLeavingPeer(ConnectionStateArgs args)
        {
            if (args.State != ConnectionState.Stopped)
            {
                return;
            }

            anchors.Remove(args.PeerId);
            if (ownedObjects.TryGetValue(args.PeerId, out List<NetworkObject> owned) && owned.Count == 0)
            {
                ownedObjects.Remove(args.PeerId);
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

        private bool DecideObserver(uint objectId, ulong peerId)
        {
            NetworkObject networkObject = spawning;
            if (networkObject == null && !spawned.TryGetValue(objectId, out networkObject))
            {
                return false;
            }

            switch (networkObject.Visibility)
            {
                case NetworkVisibility.Everyone:
                    return true;
                case NetworkVisibility.OwnerOnly:
                    return false;
            }

            ObserverRule rule = ObserverRule;
            if (rule == null)
            {
                return true;
            }

            try
            {
                return rule.Decide(this, networkObject, peerId);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
        }

        private void Resync(ulong peerId, uint objectId, byte behaviourIndex, ReadOnlyMemory<byte> body)
        {
            if (!spawned.TryGetValue(objectId, out NetworkObject networkObject) || behaviourIndex >= networkObject.Behaviours.Count)
            {
                return;
            }

            StateSlot slot = networkObject.Behaviours[behaviourIndex].StateSlot;
            if (slot == null || !Objects.IsObserver(objectId, peerId))
            {
                return;
            }

            session.SendToObject(peerId, slot.MessageId, objectId, behaviourIndex, slot.Sent.Span);
        }

        private void RouteStates(NetworkObject networkObject)
        {
            foreach (NetworkBehaviour behaviour in networkObject.Behaviours)
            {
                StateSlot slot = behaviour.StateSlot;
                if (slot == null)
                {
                    continue;
                }

                if (rpcTypes.ContainsKey(slot.MessageId))
                {
                    throw new HandlerRegistrationException($"message id 0x{slot.MessageId:X8} is already a server RPC of {rpcTypes[slot.MessageId].FullName}; {behaviour.GetType().FullName} cannot use it as a state model");
                }

                stateIds.Add(slot.MessageId);
            }
        }

        private void DeliverRpc(uint messageId, ulong peerId, uint objectId, byte behaviourIndex, ReadOnlyMemory<byte> body)
        {
            if (!spawned.TryGetValue(objectId, out NetworkObject networkObject)
                || behaviourIndex >= networkObject.Behaviours.Count
                || !networkObject.Behaviours[behaviourIndex].TryGetServerRpc(messageId, out ServerRpc rpc))
            {
                return;
            }

            if (rpc.RequireOwnership && networkObject.OwnerId != peerId)
            {
                Debug.LogWarning($"Fomoxa dropped server RPC 0x{messageId:X8} to object {objectId} from peer {peerId}, which does not own the object");
                return;
            }

            rpc.Invoke(peerId, body);
        }

        private void DespawnForLeavingOwner(uint objectId)
        {
            if (!spawned.TryGetValue(objectId, out NetworkObject networkObject))
            {
                Objects.Despawn(objectId);
                return;
            }

            try
            {
                Unspawn(networkObject, true);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void RaiseOwnerChanged(ObjectOwnerChangedArgs args)
        {
            if (!spawned.TryGetValue(args.ObjectId, out NetworkObject networkObject))
            {
                return;
            }

            RemoveOwned(args.PreviousOwnerId, networkObject);
            AddOwned(args.OwnerId, networkObject);

            bool ownerLeft = args.PreviousOwnerId != 0 && session.PeerState(args.PreviousOwnerId) != ConnectionState.Started;
            try
            {
                networkObject.OwnerChangedServer(args.PreviousOwnerId);
            }
            catch (Exception exception) when (ownerLeft)
            {
                Debug.LogException(exception);
            }
        }

        private bool ApplyInput(InputSlot slot, ReadOnlyMemory<byte> input, InputContext context, ulong ownerId)
        {
            try
            {
                slot.Apply(input, context);
                return true;
            }
            catch (MessageDecodeException)
            {
                Inputs.CountDropped(ownerId);
                return false;
            }
        }

        private void SendReconcileState(InputSlot slot, ulong ownerId, uint objectId, byte behaviourIndex, uint tick)
        {
            reconcileState.Tick = tick;
            reconcileState.Data = slot.Reconcile.Capture();
            session.SendToObject(ownerId, inputProtocol.ReconcileCodec.MessageId, objectId, behaviourIndex, inputProtocol.ReconcileCodec.Encode(reconcileState).Span);
        }

        private static void LogRejectedInput(InputRejectedArgs args)
        {
            Debug.LogWarning($"Fomoxa dropped input to object {args.ObjectId} from peer {args.PeerId}, which does not own the object");
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

        private readonly struct ReconcileSend
        {
            public ReconcileSend(NetworkObject networkObject, byte behaviourIndex, ulong ownerId)
            {
                NetworkObject = networkObject;
                BehaviourIndex = behaviourIndex;
                OwnerId = ownerId;
            }

            public NetworkObject NetworkObject { get; }

            public byte BehaviourIndex { get; }

            public ulong OwnerId { get; }
        }
    }
}
