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
        private readonly InputProtocol inputProtocol;
        private readonly InputFrames inputFrames = new InputFrames();
        private readonly List<NetworkObject> gathering = new List<NetworkObject>();
        private readonly List<NetworkObject> predicted = new List<NetworkObject>();
        private readonly List<NetworkObject> worldObjects = new List<NetworkObject>();
        private readonly List<IPhysicsSimulation> predictedWorlds = new List<IPhysicsSimulation>();
        private readonly ReplayGroups replayGroups = new ReplayGroups();
        private readonly List<PhysicsHistory> replayHistories = new List<PhysicsHistory>();
        private readonly List<HeldState> heldStates = new List<HeldState>();
        private ReconcileState reconcileState = new ReconcileState();

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
            this.inputProtocol = inputProtocol;
            RpcIds = rpcIds;
            Dispatcher = new MessageDispatcher(schema);
            session = new ClientSession(schema, sessionConfig, limits, Dispatcher, protocol);
            session.OnHandlerException += RaiseHandlerException;
            Entities = new ClientEntities(this, session, Dispatcher, channels, stateProtocol, transformProtocol, rpcIds, serverManager.Entities, new EntityBackend(this, prefabs), UnityNetworkLog.Instance);
            Entities.OnDespawning += record => Physics?.EndProxy((NetworkObject)record.Representation);
            Dispatcher.RegisterObject(inputProtocol.ReconcileCodec.MessageId, (peerId, objectId, behaviourIndex, body) => HoldReconcileState(objectId, behaviourIndex, body));
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

        public bool IsReplaying { get; private set; }

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

        internal bool SimulatesPhysics { get; set; }

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

        internal void PlaceProxies()
        {
            if (connectedLocally || session.State != ConnectionState.Started)
            {
                return;
            }

            Physics.ForgetDestroyedProxies();
            foreach (EntityRecord record in Entities.Spawned.Values)
            {
                var networkObject = (NetworkObject)record.Representation;
                if (networkObject.IsPredicting)
                {
                    Physics.EndProxy(networkObject);
                }
                else
                {
                    Physics.PlaceProxy(networkObject);
                }
            }
        }

        internal void EndProxies()
        {
            foreach (EntityRecord record in Entities.Spawned.Values)
            {
                var networkObject = (NetworkObject)record.Representation;
                Physics.EndProxy(networkObject);
            }
        }

        internal void Predict(uint predictionTick)
        {
            predicted.Clear();
            if (connectedLocally || session.State != ConnectionState.Started || !timeManager.ClockSynced)
            {
                return;
            }

            gathering.Clear();
            foreach (EntityRecord record in Entities.Spawned.Values)
            {
                var networkObject = (NetworkObject)record.Representation;
                if (record.HasInput)
                {
                    gathering.Add(networkObject);
                }
            }

            foreach (NetworkObject networkObject in gathering)
            {
                if (Objects.IsOwner(networkObject.ObjectId))
                {
                    predicted.Add(networkObject);
                }
                else
                {
                    StopPredicting(networkObject);
                }
            }

            gathering.Clear();
            if (SimulatesPhysics)
            {
                ReconcileWorlds();
            }
            else
            {
                foreach (NetworkObject networkObject in predicted)
                {
                    Reconcile(networkObject);
                }
            }

            foreach (NetworkObject networkObject in predicted)
            {
                PredictOwned(networkObject, predictionTick);
            }
        }

        internal void CapturePredicted(uint predictionTick)
        {
            if (!SimulatesPhysics)
            {
                predicted.Clear();
                return;
            }

            predictedWorlds.Clear();
            foreach (NetworkObject networkObject in predicted)
            {
                if (networkObject == null || networkObject.Client != this)
                {
                    continue;
                }

                bool capturedAny = false;
                foreach (NetworkBehaviour behaviour in networkObject.Behaviours)
                {
                    InputSlot slot = behaviour.InputSlot;
                    if (slot?.Reconcile != null && slot.Predicting)
                    {
                        capturedAny = true;
                        CaptureAt(slot, predictionTick);
                    }
                }

                if (capturedAny)
                {
                    AddWorld(Physics.Of(networkObject.gameObject.scene));
                    AddWorld(Physics.Of2D(networkObject.gameObject.scene));
                }
            }

            foreach (IPhysicsSimulation world in predictedWorlds)
            {
                Physics.HistoryOf(world, InputRules.History).Save(predictionTick);
            }

            predictedWorlds.Clear();
            predicted.Clear();
        }

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

        private void HoldReconcileState(uint objectId, byte behaviourIndex, ReadOnlyMemory<byte> body)
        {
            if (connectedLocally
                || !TryGetObject(objectId, out NetworkObject networkObject)
                || !Objects.IsOwner(objectId)
                || behaviourIndex >= networkObject.Behaviours.Count)
            {
                return;
            }

            InputSlot slot = networkObject.Behaviours[behaviourIndex].InputSlot;
            if (slot?.Reconcile == null)
            {
                return;
            }

            inputProtocol.ReconcileCodec.Decode(body, ref reconcileState);
            slot.HoldPending(reconcileState.Tick, reconcileState.Data.Span);
        }

        private void Reconcile(NetworkObject networkObject)
        {
            IReadOnlyList<NetworkBehaviour> behaviours = networkObject.Behaviours;
            bool correcting = false;
            bool replayedAny = false;
            for (int index = 0; index < behaviours.Count; index++)
            {
                InputSlot slot = behaviours[index].InputSlot;
                if (slot?.Reconcile == null || !slot.HasPending)
                {
                    continue;
                }

                if (!correcting)
                {
                    correcting = true;
                    BeginCorrection(networkObject);
                }

                if (!slot.Predicting)
                {
                    ResetTransforms(networkObject);
                }

                uint tick = slot.PendingTick;
                try
                {
                    bool replayed = slot.ReconcilePending();
                    slot.Predicting = true;
                    if (replayed)
                    {
                        replayedAny = true;
                        behaviours[index].Reconciled(tick);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            if (replayedAny)
            {
                EndCorrection(networkObject);
            }
        }

        private void ReconcileWorlds()
        {
            replayGroups.Clear();
            foreach (NetworkObject networkObject in predicted)
            {
                Scene scene = networkObject.gameObject.scene;
                replayGroups.Add(Physics.Of(scene), Physics.Of2D(scene));
            }

            foreach (ReplayGroup group in replayGroups.Groups)
            {
                worldObjects.Clear();
                foreach (NetworkObject networkObject in predicted)
                {
                    if (group.Worlds.Contains(Physics.Of(networkObject.gameObject.scene)))
                    {
                        worldObjects.Add(networkObject);
                    }
                }

                ReconcileGroup(group, worldObjects);
            }

            worldObjects.Clear();
            replayGroups.Clear();
        }

        private void ReconcileGroup(ReplayGroup group, List<NetworkObject> objects)
        {
            if (!TryFindNewestPending(objects, out uint target))
            {
                return;
            }

            heldStates.Clear();
            bool mismatched = false;
            foreach (NetworkObject networkObject in objects)
            {
                IReadOnlyList<NetworkBehaviour> behaviours = networkObject.Behaviours;
                for (int index = 0; index < behaviours.Count; index++)
                {
                    InputSlot slot = behaviours[index].InputSlot;
                    if (slot?.Reconcile == null || !slot.HasPending)
                    {
                        continue;
                    }

                    ReadOnlyMemory<byte> server = slot.TakePending(out uint tick);
                    if (tick != target)
                    {
                        slot.Forget(tick);
                        continue;
                    }

                    bool matched = false;
                    try
                    {
                        matched = slot.MatchesAt(target, server);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }

                    heldStates.Add(new HeldState(networkObject, behaviours[index], server, matched));
                    mismatched |= !matched;
                }
            }

            if (mismatched)
            {
                Replay(group, objects, target);
            }

            foreach (HeldState held in heldStates)
            {
                held.Behaviour.InputSlot.Forget(target);
                if (!held.Matched)
                {
                    held.Behaviour.Reconciled(target);
                }
            }

            heldStates.Clear();
        }

        private void Replay(ReplayGroup group, List<NetworkObject> objects, uint target)
        {
            foreach (NetworkObject networkObject in objects)
            {
                BeginCorrection(networkObject);
            }

            foreach (HeldState held in heldStates)
            {
                if (!held.Behaviour.InputSlot.Predicting)
                {
                    ResetTransforms(held.NetworkObject);
                    Physics.EndProxy(held.NetworkObject);
                }
            }

            IsReplaying = true;
            bool loaded = LoadGroup(group, target);
            if (loaded)
            {
                group.RestoreContacts(target);
            }

            foreach (HeldState held in heldStates)
            {
                InputSlot slot = held.Behaviour.InputSlot;
                try
                {
                    slot.Reconcile.Restore(held.Server);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }

                slot.Predicting = true;
            }

            uint newest = NewestPredictedTick(objects, target);
            float seconds = (float)timeManager.TickDelta;
            for (uint tick = target + 1; tick <= newest && tick > target; tick++)
            {
                foreach (NetworkObject networkObject in objects)
                {
                    foreach (NetworkBehaviour behaviour in networkObject.Behaviours)
                    {
                        InputSlot slot = behaviour.InputSlot;
                        if (slot?.Reconcile != null && slot.Predicting)
                        {
                            ReplayInput(slot, tick);
                        }
                    }
                }

                foreach (UnityPhysicsWorld world in group.Worlds)
                {
                    world.Step(seconds);
                }

                foreach (UnityPhysicsWorld2D world in group.Worlds2D)
                {
                    world.Step(seconds);
                }

                group.QueryContacts(tick, loaded, InputRules.History);
                foreach (NetworkObject networkObject in objects)
                {
                    foreach (NetworkBehaviour behaviour in networkObject.Behaviours)
                    {
                        InputSlot slot = behaviour.InputSlot;
                        if (slot?.Reconcile != null && slot.Predicting)
                        {
                            CaptureAt(slot, tick);
                        }
                    }
                }

                if (!loaded)
                {
                    continue;
                }

                foreach (PhysicsHistory history in replayHistories)
                {
                    history.Save(tick);
                }
            }

            replayHistories.Clear();
            IsReplaying = false;
            group.PublishContacts();
            foreach (NetworkObject networkObject in objects)
            {
                EndCorrection(networkObject);
            }
        }

        private bool LoadGroup(ReplayGroup group, uint target)
        {
            replayHistories.Clear();
            foreach (UnityPhysicsWorld world in group.Worlds)
            {
                replayHistories.Add(Physics.HistoryOf(world, InputRules.History));
            }

            foreach (UnityPhysicsWorld2D world in group.Worlds2D)
            {
                replayHistories.Add(Physics.HistoryOf(world, InputRules.History));
            }

            foreach (PhysicsHistory history in replayHistories)
            {
                if (!history.Has(target))
                {
                    return false;
                }
            }

            foreach (PhysicsHistory history in replayHistories)
            {
                history.Load(target);
            }

            return true;
        }

        private static bool TryFindNewestPending(List<NetworkObject> objects, out uint target)
        {
            target = 0;
            bool found = false;
            foreach (NetworkObject networkObject in objects)
            {
                foreach (NetworkBehaviour behaviour in networkObject.Behaviours)
                {
                    InputSlot slot = behaviour.InputSlot;
                    if (slot?.Reconcile != null && slot.HasPending && (!found || slot.PendingTick > target))
                    {
                        target = slot.PendingTick;
                        found = true;
                    }
                }
            }

            return found;
        }

        private static uint NewestPredictedTick(List<NetworkObject> objects, uint target)
        {
            uint newest = target;
            foreach (NetworkObject networkObject in objects)
            {
                foreach (NetworkBehaviour behaviour in networkObject.Behaviours)
                {
                    InputSlot slot = behaviour.InputSlot;
                    if (slot?.Reconcile != null && slot.Predicting && slot.HistoryCount > 0 && slot.NewestTick > newest)
                    {
                        newest = slot.NewestTick;
                    }
                }
            }

            return newest;
        }

        private static void ReplayInput(InputSlot slot, uint tick)
        {
            try
            {
                slot.ReplayInput(tick);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void CaptureAt(InputSlot slot, uint tick)
        {
            try
            {
                slot.CaptureAt(tick);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void AddWorld(IPhysicsSimulation world)
        {
            if (!predictedWorlds.Contains(world))
            {
                predictedWorlds.Add(world);
            }
        }

        private void PredictOwned(NetworkObject networkObject, uint predictionTick)
        {
            IReadOnlyList<NetworkBehaviour> behaviours = networkObject.Behaviours;
            for (int index = 0; index < behaviours.Count; index++)
            {
                InputSlot slot = behaviours[index].InputSlot;
                if (slot == null)
                {
                    continue;
                }

                try
                {
                    slot.Record(predictionTick, slot.Gather().Span);
                    if (slot.Reconcile != null && slot.Predicting)
                    {
                        slot.ApplyGathered(new InputContext(predictionTick, false));
                        if (!SimulatesPhysics)
                        {
                            slot.StoreState(predictionTick, slot.Reconcile.Capture().Span);
                        }
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    continue;
                }

                inputFrames.Tick = predictionTick;
                slot.CopyFramesTo(inputFrames.Frames);
                session.SendToObject(inputProtocol.FramesCodec.MessageId, networkObject.ObjectId, (byte)index, inputProtocol.FramesCodec.Encode(inputFrames).Span);
            }
        }

        private static void StopPredicting(NetworkObject networkObject)
        {
            bool wasPredicting = networkObject.IsPredicting;
            foreach (NetworkBehaviour behaviour in networkObject.Behaviours)
            {
                InputSlot slot = behaviour.InputSlot;
                if (slot != null)
                {
                    slot.ClearHistory();
                    slot.Predicting = false;
                }
            }

            if (wasPredicting)
            {
                ResetTransforms(networkObject);
            }
        }

        private static void ResetTransforms(NetworkObject networkObject)
        {
            foreach (NetworkBehaviour behaviour in networkObject.Behaviours)
            {
                (behaviour as NetworkTransform)?.ResetReceive();
            }
        }

        private static void BeginCorrection(NetworkObject networkObject)
        {
            foreach (NetworkBehaviour behaviour in networkObject.Behaviours)
            {
                (behaviour as NetworkTransform)?.BeginCorrection();
            }
        }

        private static void EndCorrection(NetworkObject networkObject)
        {
            foreach (NetworkBehaviour behaviour in networkObject.Behaviours)
            {
                (behaviour as NetworkTransform)?.EndCorrection();
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

        private readonly struct HeldState
        {
            public HeldState(NetworkObject networkObject, NetworkBehaviour behaviour, ReadOnlyMemory<byte> server, bool matched)
            {
                NetworkObject = networkObject;
                Behaviour = behaviour;
                Server = server;
                Matched = matched;
            }

            public NetworkObject NetworkObject { get; }

            public NetworkBehaviour Behaviour { get; }

            public ReadOnlyMemory<byte> Server { get; }

            public bool Matched { get; }
        }
    }
}
