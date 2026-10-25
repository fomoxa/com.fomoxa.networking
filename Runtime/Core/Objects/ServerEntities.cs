using System;
using System.Collections.Generic;
using System.Numerics;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Prediction;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Objects
{
    internal sealed class ServerEntities
    {
        private static readonly Comparison<EntityRecord> BySpawnSequence = (first, second) => first.SpawnSequence.CompareTo(second.SpawnSequence);

        private readonly ServerSession session;
        private readonly MessageDispatcher dispatcher;
        private readonly MessageChannels channels;
        private readonly StateProtocol stateProtocol;
        private readonly InputProtocol inputProtocol;
        private readonly TransformProtocol transformProtocol;
        private readonly RpcMessageIds rpcIds;
        private readonly IServerEntityBackend backend;
        private readonly NetworkLog log;
        private readonly Dictionary<uint, EntityRecord> spawned = new Dictionary<uint, EntityRecord>();
        private readonly LinkedList<EntityRecord> spawnOrder = new LinkedList<EntityRecord>();
        private readonly LinkedList<EntityRecord> inputOrder = new LinkedList<EntityRecord>();
        private readonly LinkedList<EntityRecord> stateOrder = new LinkedList<EntityRecord>();
        private List<EntityRecord> queuedStates = new List<EntityRecord>();
        private List<EntityRecord> syncingStates = new List<EntityRecord>();
        private readonly LinkedList<EntityRecord> transformOrder = new LinkedList<EntityRecord>();
        private List<EntityRecord> queuedTransforms = new List<EntityRecord>();
        private List<EntityRecord> syncingTransforms = new List<EntityRecord>();
        private readonly List<INetworkEntity> moved = new List<INetworkEntity>();
        private long spawnSequence;
        private readonly HashSet<INetworkEntity> sceneObjects = new HashSet<INetworkEntity>();
        private readonly Dictionary<ulong, List<EntityRecord>> owned = new Dictionary<ulong, List<EntityRecord>>();
        private readonly Dictionary<uint, Type> rpcTypes = new Dictionary<uint, Type>();
        private readonly HashSet<uint> stateIds = new HashSet<uint>();
        private readonly StateDelta stateDelta = new StateDelta();
        private readonly ReconcileState reconcileState = new ReconcileState();
        private readonly List<EntityRecord> applying = new List<EntityRecord>();
        private readonly List<ReconcileSend> reconciling = new List<ReconcileSend>();
        private readonly List<ReadOnlyMemory<byte>> spawnStates = new List<ReadOnlyMemory<byte>>();
        private readonly List<ReadOnlyMemory<byte>> spawnCollections = new List<ReadOnlyMemory<byte>>();
        private readonly ByteSegments spawnCollectionBytes = new ByteSegments();
        private readonly CollectionContents spawnContents = new CollectionContents();
        private readonly CollectionChanges collectionChanges = new CollectionChanges();
        private CollectionResync collectionResync = new CollectionResync();
        private readonly Dictionary<ulong, AnchorPositions> anchors = new Dictionary<ulong, AnchorPositions>();
        private readonly List<ObserverAddedArgs> enteredObservers = new List<ObserverAddedArgs>();
        private readonly TransformUpdate transformUpdate = new TransformUpdate();
        private readonly TransformSettle transformSettle = new TransformSettle();
        private int anchorGeneration = 1;
        private static readonly List<EntityRecord> NoRecords = new List<EntityRecord>();

        public ServerEntities(
            object owner,
            ServerSession session,
            ServerObjects objects,
            ServerInputs inputs,
            MessageDispatcher dispatcher,
            MessageChannels channels,
            StateProtocol stateProtocol,
            InputProtocol inputProtocol,
            TransformProtocol transformProtocol,
            RpcMessageIds rpcIds,
            IServerEntityBackend backend,
            NetworkLog log)
        {
            Owner = owner;
            this.session = session;
            Objects = objects;
            Inputs = inputs;
            this.dispatcher = dispatcher;
            this.channels = channels;
            this.stateProtocol = stateProtocol;
            this.inputProtocol = inputProtocol;
            this.transformProtocol = transformProtocol;
            this.rpcIds = rpcIds;
            this.backend = backend;
            this.log = log;
            Representations = new RepresentationTable(spawned);
            Observers = new ObserverContext(this);
            Groups = new ObserverGroups(this);
            dispatcher.RegisterObject(stateProtocol.ResyncCodec.MessageId, Resync);
            dispatcher.RegisterObject(stateProtocol.Collections.ResyncCodec.MessageId, ResyncCollection);
            inputs.OnInputRejected += LogRejectedInput;
            objects.Observes = DecideObserver;
            objects.CollectCandidates = CollectCandidates;
            objects.OnOwnerChanged += RaiseOwnerChanged;
            objects.DespawnWithOwner = objectId => !spawned.TryGetValue(objectId, out EntityRecord record) || record.Representation.DespawnWithOwner;
            objects.Despawner = DespawnForLeavingOwner;
            objects.OnObserverAdded += enteredObservers.Add;
            session.OnRemoteConnectionState += ForgetLeavingPeer;
            session.OnServerConnectionState += DespawnAllWhenStopped;
        }

        public event Action<EntityRecord> OnSpawned;

        public event Action<EntityRecord> OnUnspawning;

        public object Owner { get; }

        public ServerObjects Objects { get; }

        public ServerInputs Inputs { get; }

        public InputRules InputRules { get; set; } = new InputRules();

        public IObserverRule ObserverRule { get; set; }

        public ObserverContext Observers { get; }

        public ObserverGroups Groups { get; }

        public IReadOnlyDictionary<uint, EntityRecord> Spawned => spawned;

        public IReadOnlyDictionary<uint, INetworkEntity> Representations { get; }

        public LinkedList<EntityRecord> InSpawnOrder => spawnOrder;

        public EntityRecord Spawning { get; private set; }

        public bool TryGet(uint objectId, out EntityRecord record) => spawned.TryGetValue(objectId, out record);

        public List<EntityRecord> OwnedBy(ulong peerId) =>
            owned.TryGetValue(peerId, out List<EntityRecord> records) ? records : NoRecords;

        public bool IsSceneObject(INetworkEntity entity) => sceneObjects.Contains(entity);

        public void ResetSceneObjects() => sceneObjects.Clear();

        public void ForgetSceneObjects(Predicate<INetworkEntity> match) => sceneObjects.RemoveWhere(match);

        public void SpawnSceneObjects(IReadOnlyList<INetworkEntity> found)
        {
            foreach (INetworkEntity sceneObject in found)
            {
                if (sceneObject.Record == null)
                {
                    sceneObjects.Add(sceneObject);
                }
            }

            foreach (INetworkEntity sceneObject in found)
            {
                if (sceneObject.Record != null || !sceneObjects.Contains(sceneObject))
                {
                    continue;
                }

                try
                {
                    Spawn(sceneObject, 0);
                }
                catch (Exception exception)
                {
                    log.Exception(exception);
                }
            }
        }

        public void Spawn(INetworkEntity entity, ulong ownerId)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (session.State != ServerState.Started)
            {
                throw new InvalidOperationException("the server is not started");
            }

            if (entity.Record != null)
            {
                throw new ArgumentException($"{backend.NameOf(entity)} is already spawned", nameof(entity));
            }

            backend.ValidateSpawn(entity);
            bool isSceneObject = sceneObjects.Contains(entity);
            uint fingerprint = backend.FingerprintOf(entity, isSceneObject);
            backend.PrepareSpawn(entity);
            var record = new EntityRecord(entity, fingerprint);
            IReadOnlyList<EntityBehaviour> behaviours = entity.EntityBehaviours;
            bool comparesState = false;
            foreach (EntityBehaviour behaviour in behaviours)
            {
                behaviour.Register(rpcIds, channels, stateProtocol, InputRules);
                record.HasInput |= behaviour.InputSlot != null;
                if (behaviour.StateSlot != null)
                {
                    behaviour.StateSlot.Record = record;
                    comparesState |= !behaviour.StateSlot.OnlyWhenDirty;
                    record.HasMarkedState |= behaviour.StateSlot.OnlyWhenDirty;
                }

                foreach (CollectionSlot slot in behaviour.Collections)
                {
                    slot.Record = record;
                    slot.ClearPending();
                }
            }

            CaptureTransforms(record, behaviours);

            RouteRpcs(behaviours);
            RouteStates(behaviours);
            CaptureStates(behaviours, true);
            uint sceneId = backend.SceneIdOf(entity);
            uint objectId;
            anchorGeneration++;
            record.GroupIds = Groups.GroupIdsOf(entity);
            Spawning = record;
            try
            {
                objectId = isSceneObject
                    ? Objects.SpawnScene(entity.SceneObjectId, ownerId, sceneId)
                    : Objects.Spawn(entity.PrefabId, ownerId, sceneId);
            }
            finally
            {
                Spawning = null;
            }

            if (isSceneObject)
            {
                backend.Activate(entity);
            }

            record.ObjectId = objectId;
            record.Server = this;
            entity.Bind(record);
            spawned.Add(objectId, record);
            record.SpawnOrderNode = spawnOrder.AddLast(record);
            record.SpawnSequence = ++spawnSequence;
            if (comparesState)
            {
                record.StateOrderNode = stateOrder.AddLast(record);
            }

            QueueSpawnedTransforms(record);
            if (record.HasInput)
            {
                record.InputOrderNode = inputOrder.AddLast(record);
            }

            bool firstOwned = AddOwned(ownerId, record);
            try
            {
                foreach (EntityBehaviour behaviour in behaviours)
                {
                    behaviour.OnStartServer();
                }
            }
            finally
            {
                OnSpawned?.Invoke(record);
                if (firstOwned)
                {
                    RebuildForFirstAnchor(ownerId);
                }
            }
        }

        public bool Despawn(INetworkEntity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            return Unspawn(entity.Record, true);
        }

        public void DespawnDestroyed(INetworkEntity entity)
        {
            Unspawn(entity.Record, false);
            sceneObjects.Remove(entity);
        }

        public bool ChangeOwner(INetworkEntity entity, ulong ownerId)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            EntityRecord record = entity.Record;
            if (record == null || record.Server != this)
            {
                return false;
            }

            anchorGeneration++;
            ulong previousOwnerId = record.OwnerId;
            if (!Objects.ChangeOwner(record.ObjectId, ownerId))
            {
                return false;
            }

            if (previousOwnerId != ownerId && owned.TryGetValue(ownerId, out List<EntityRecord> records) && records.Count == 1)
            {
                RebuildForFirstAnchor(ownerId);
            }

            return true;
        }

        public bool IsObserver(INetworkEntity entity, ulong peerId)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            EntityRecord record = entity.Record;
            return record != null && record.Server == this && Objects.IsObserver(record.ObjectId, peerId);
        }

        public bool RebuildObservers(INetworkEntity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            anchorGeneration++;
            EntityRecord record = entity.Record;
            return record != null && record.Server == this && Objects.RebuildObservers(record.ObjectId);
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

        public void RebuildObserversRound()
        {
            IObserverRule rule = ObserverRule;
            if (rule == null || !rule.RebuildsPeriodically)
            {
                return;
            }

            anchorGeneration++;
            Objects.RebuildObserversRound();
        }

        public void NextAnchorRound() => anchorGeneration++;

        public IReadOnlyList<Vector3> AnchorsOf(ulong peerId)
        {
            List<EntityRecord> records = OwnedBy(peerId);
            if (records.Count == 0)
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
                foreach (EntityRecord record in records)
                {
                    cached.Positions.Add(record.Representation.ReadWorldPosition());
                }
            }

            return cached.Positions;
        }

        public SpawnData ReadSpawnData(uint objectId)
        {
            EntityRecord record = Spawning;
            if (record == null && !spawned.TryGetValue(objectId, out record))
            {
                throw new InvalidOperationException($"object {objectId} has no representation on the server");
            }

            INetworkEntity entity = record.Representation;
            spawnStates.Clear();
            IReadOnlyList<EntityBehaviour> behaviours = entity.EntityBehaviours;
            if (record.StatesStale)
            {
                CaptureStates(behaviours, false);
                record.StatesStale = false;
            }

            for (int index = 0; index < behaviours.Count; index++)
            {
                spawnStates.Add(behaviours[index].StateSlot?.Sent ?? ReadOnlyMemory<byte>.Empty);
            }

            WriteSpawnCollections(behaviours);
            entity.ReadRootPose(out Vector3 worldPosition, out Quaternion worldRotation, out Vector3 localScale);
            return new SpawnData(record.Fingerprint, worldPosition, worldRotation, localScale, spawnStates, spawnCollections);
        }

        public void SyncTransforms(uint tick)
        {
            foreach (ObserverAddedArgs entered in enteredObservers)
            {
                if (!spawned.TryGetValue(entered.ObjectId, out EntityRecord enteredRecord) || !Objects.IsObserver(entered.ObjectId, entered.PeerId))
                {
                    continue;
                }

                IReadOnlyList<EntityBehaviour> enteredBehaviours = enteredRecord.Representation.EntityBehaviours;
                for (int index = 0; index < enteredBehaviours.Count; index++)
                {
                    TransformSlot slot = enteredBehaviours[index].TransformSlot;
                    if (slot != null && slot.Sync.SettlePending)
                    {
                        QueueTransform(enteredRecord);
                    }
                    else if (slot != null)
                    {
                        slot.Source.ReadLocal(out Vector3 position, out Quaternion rotation, out Vector3 scale);
                        if (enteredRecord.TransformsStale)
                        {
                            slot.Sync.MarkSettled(position, rotation, scale);
                        }

                        ReadOnlySpan<byte> settle = EncodeSettle(slot.Sync, tick, slot.Sync.SelectedMask, position, rotation, scale);
                        session.SendToObject(entered.PeerId, transformProtocol.SettleCodec.MessageId, enteredRecord.ObjectId, (byte)index, settle);
                    }
                }
            }

            enteredObservers.Clear();
            (queuedTransforms, syncingTransforms) = (syncingTransforms, queuedTransforms);
            if (syncingTransforms.Count > 1)
            {
                syncingTransforms.Sort(BySpawnSequence);
            }

            LinkedListNode<EntityRecord> node = transformOrder.First;
            int queued = 0;
            while (node != null || queued < syncingTransforms.Count)
            {
                EntityRecord record;
                if (queued < syncingTransforms.Count && (node == null || syncingTransforms[queued].SpawnSequence < node.Value.SpawnSequence))
                {
                    record = syncingTransforms[queued++];
                    record.TransformQueued = false;
                    if (record.Server != this)
                    {
                        continue;
                    }
                }
                else
                {
                    record = node.Value;
                    node = node.Next;
                }

                SyncTransform(record, tick);
            }

            syncingTransforms.Clear();
        }

        public IPhysicsMotion Motion { get; set; }

        public void CollectMotion(IPhysicsMotion motion)
        {
            moved.Clear();
            motion.CollectMoved(moved);
            foreach (INetworkEntity entity in moved)
            {
                EntityRecord record = entity.Record;
                if (record != null && record.Server == this)
                {
                    SignalTransform(record);
                }
            }

            moved.Clear();
        }

        internal void SignalTransform(EntityRecord record)
        {
            record.TransformSignaled = true;
            QueueTransform(record);
        }

        private void QueueTransform(EntityRecord record)
        {
            if (record.TransformQueued || record.TransformOrderNode != null)
            {
                return;
            }

            record.TransformQueued = true;
            queuedTransforms.Add(record);
        }

        private void SyncTransform(EntityRecord record, uint tick)
        {
            bool signaled = record.TransformSignaled;
            record.TransformSignaled = false;
            if (Objects.ObserversOf(record.ObjectId).Count == 0)
            {
                record.TransformsStale = true;
                return;
            }

            record.TransformsStale = false;
            bool polled = false;
            bool moving = false;
            IReadOnlyList<EntityBehaviour> behaviours = record.Representation.EntityBehaviours;
            for (int index = 0; index < behaviours.Count; index++)
            {
                TransformSlot slot = behaviours[index].TransformSlot;
                if (slot == null)
                {
                    continue;
                }

                TransformSync sync = slot.Sync;
                if (slot.Tracked && !signaled && !sync.Moving && !sync.SettlePending)
                {
                    continue;
                }

                slot.Tracked = Follows(record, slot);
                slot.Source.ReadLocal(out Vector3 position, out Quaternion rotation, out Vector3 scale);
                if (sync.SettlePending)
                {
                    sync.SettlePending = false;
                    sync.MarkSettled(position, rotation, scale);
                    Objects.SendToObservers(transformProtocol.SettleCodec.MessageId, record.ObjectId, (byte)index, EncodeSettle(sync, tick, sync.SelectedMask, position, rotation, scale));
                }
                else
                {
                    TransformSend send = sync.Sample(position, rotation, scale, out byte mask);
                    if (send == TransformSend.Update)
                    {
                        transformUpdate.Tick = tick;
                        transformUpdate.Mask = mask;
                        transformUpdate.Generation = sync.Generation;
                        SpawnTransform.PackSelected(mask, position, rotation, scale, transformUpdate.Values);
                        Objects.SendToObservers(transformProtocol.UpdateCodec.MessageId, record.ObjectId, (byte)index, transformProtocol.UpdateCodec.Encode(transformUpdate).Span);
                    }
                    else if (send == TransformSend.Settle)
                    {
                        Objects.SendToObservers(transformProtocol.SettleCodec.MessageId, record.ObjectId, (byte)index, EncodeSettle(sync, tick, mask, position, rotation, scale));
                    }
                }

                polled |= !slot.Tracked;
                moving |= slot.Tracked && sync.Moving;
            }

            PlaceInTransformOrder(record, polled);
            if (moving && !polled)
            {
                QueueTransform(record);
            }
        }

        private bool Follows(EntityRecord record, TransformSlot slot)
        {
            switch (slot.Target)
            {
                case TransformTarget.EntityPose:
                    return record.Representation is IPoseSignals signals
                        && signals.SignalsPose
                        && (!signals.HasBody || (Motion != null && Motion.ReportsMotion(record.Representation)));
                case TransformTarget.BodyRoot:
                    return !slot.Sync.Settings.SyncScale && Motion != null && Motion.ReportsMotion(record.Representation);
                default:
                    return false;
            }
        }

        private void QueueSpawnedTransforms(EntityRecord record)
        {
            foreach (EntityBehaviour behaviour in record.Representation.EntityBehaviours)
            {
                if (behaviour.TransformSlot != null)
                {
                    SignalTransform(record);
                    return;
                }
            }
        }

        private void PlaceInTransformOrder(EntityRecord record, bool polled)
        {
            if (polled == (record.TransformOrderNode != null))
            {
                return;
            }

            if (!polled)
            {
                transformOrder.Remove(record.TransformOrderNode);
                record.TransformOrderNode = null;
                return;
            }

            LinkedListNode<EntityRecord> after = transformOrder.Last;
            while (after != null && after.Value.SpawnSequence > record.SpawnSequence)
            {
                after = after.Previous;
            }

            record.TransformOrderNode = after == null ? transformOrder.AddFirst(record) : transformOrder.AddAfter(after, record);
        }

        public void SyncStates()
        {
            (queuedStates, syncingStates) = (syncingStates, queuedStates);
            if (syncingStates.Count > 1)
            {
                syncingStates.Sort(BySpawnSequence);
            }

            LinkedListNode<EntityRecord> node = stateOrder.First;
            int queued = 0;
            while (node != null || queued < syncingStates.Count)
            {
                EntityRecord record;
                if (queued < syncingStates.Count && (node == null || syncingStates[queued].SpawnSequence < node.Value.SpawnSequence))
                {
                    record = syncingStates[queued++];
                    record.StateQueued = false;
                    if (record.Server != this)
                    {
                        continue;
                    }
                }
                else
                {
                    record = node.Value;
                    node = node.Next;
                }

                SyncState(record);
            }

            syncingStates.Clear();
        }

        internal void QueueState(EntityRecord record)
        {
            if (record.StateQueued || record.StateOrderNode != null)
            {
                return;
            }

            record.StateQueued = true;
            queuedStates.Add(record);
            if (Objects.ObserversOf(record.ObjectId).Count == 0)
            {
                record.StatesStale = true;
            }
        }

        private void SyncState(EntityRecord record)
        {
            IReadOnlyList<EntityBehaviour> behaviours = record.Representation.EntityBehaviours;
            if (Objects.ObserversOf(record.ObjectId).Count == 0)
            {
                record.StatesStale = true;
                foreach (EntityBehaviour behaviour in behaviours)
                {
                    foreach (CollectionSlot slot in behaviour.Collections)
                    {
                        slot.ClearPending();
                    }
                }

                return;
            }

            bool marked = record.HasMarkedState;
            for (int index = 0; index < behaviours.Count; index++)
            {
                SyncBehaviourState(record, behaviours[index], (byte)index, marked);
                SyncCollections(record, behaviours[index], (byte)index);
            }
        }

        private void SyncBehaviourState(EntityRecord record, EntityBehaviour behaviour, byte index, bool marked)
        {
            StateSlot slot = behaviour.StateSlot;
            if (slot == null || (marked && !slot.NeedsCapture))
            {
                return;
            }

            behaviour.PrepareState();
            if (!slot.Capture())
            {
                return;
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

            Objects.SendToObservers(messageId, record.ObjectId, index, body);
        }

        private void SyncCollections(EntityRecord record, EntityBehaviour behaviour, byte index)
        {
            foreach (CollectionSlot slot in behaviour.Collections)
            {
                if (!slot.HasPending)
                {
                    continue;
                }

                slot.FillChanges(collectionChanges);
                IMessageCodec<CollectionChanges> codec = stateProtocol.Collections.ChangesCodec;
                ReadOnlySpan<byte> body = codec.Encode(collectionChanges).Span;
                slot.ClearPending();
                Objects.SendToObservers(codec.MessageId, record.ObjectId, index, body);
            }
        }

        private void WriteSpawnCollections(IReadOnlyList<EntityBehaviour> behaviours)
        {
            spawnCollectionBytes.Clear();
            foreach (EntityBehaviour behaviour in behaviours)
            {
                if (behaviour.Collections.Count == 0)
                {
                    spawnCollectionBytes.Add(ReadOnlySpan<byte>.Empty);
                    continue;
                }

                spawnContents.Collections.Clear();
                foreach (CollectionSlot slot in behaviour.Collections)
                {
                    spawnContents.Collections.Add(slot.Content());
                }

                spawnCollectionBytes.Add(stateProtocol.Collections.ContentsCodec.Encode(spawnContents).Span);
            }

            spawnCollectionBytes.CopyTo(spawnCollections);
        }

        public void ApplyInputs(uint tick, ulong hostPeerId)
        {
            applying.Clear();
            foreach (EntityRecord record in inputOrder)
            {
                applying.Add(record);
            }

            foreach (EntityRecord record in applying)
            {
                if (record.Server != this || !Objects.TryGet(record.ObjectId, out ObjectRow row) || row.OwnerId == 0)
                {
                    continue;
                }

                IReadOnlyList<EntityBehaviour> behaviours = record.Representation.EntityBehaviours;
                for (int index = 0; index < behaviours.Count; index++)
                {
                    InputSlot slot = behaviours[index].InputSlot;
                    if (slot == null || record.Server != this)
                    {
                        continue;
                    }

                    try
                    {
                        if (hostPeerId != 0 && row.OwnerId == hostPeerId)
                        {
                            slot.GatherAndApply(new InputContext(tick, false));
                        }
                        else if (Inputs.TakeInput(record.ObjectId, (byte)index, tick, out ReadOnlyMemory<byte> input, out bool repeated, out uint repeatedTicks)
                            && ApplyInput(slot, input, new InputContext(tick, repeated, false, repeatedTicks), row.OwnerId)
                            && slot.Reconcile != null
                            && tick % (uint)InputRules.ReconcileInterval == 0)
                        {
                            reconciling.Add(new ReconcileSend(record, (byte)index, row.OwnerId));
                        }
                    }
                    catch (Exception exception)
                    {
                        log.Exception(exception);
                    }
                }
            }

            applying.Clear();
        }

        public void SendReconcileStates(uint tick)
        {
            foreach (ReconcileSend send in reconciling)
            {
                EntityRecord record = send.Record;
                if (record.Server != this)
                {
                    continue;
                }

                try
                {
                    SendReconcileState(record.Representation.EntityBehaviours[send.BehaviourIndex].InputSlot, send.OwnerId, record.ObjectId, send.BehaviourIndex, tick);
                }
                catch (Exception exception)
                {
                    log.Exception(exception);
                }
            }

            reconciling.Clear();
        }

        private bool Unspawn(EntityRecord record, bool destroy)
        {
            if (record == null || record.Server != this || !spawned.Remove(record.ObjectId))
            {
                return false;
            }

            uint objectId = record.ObjectId;
            INetworkEntity entity = record.Representation;
            spawnOrder.Remove(record.SpawnOrderNode);
            record.SpawnOrderNode = null;
            if (record.StateOrderNode != null)
            {
                stateOrder.Remove(record.StateOrderNode);
                record.StateOrderNode = null;
            }
            if (record.TransformOrderNode != null)
            {
                transformOrder.Remove(record.TransformOrderNode);
                record.TransformOrderNode = null;
            }
            Groups.ForgetEntity(entity);
            if (record.InputOrderNode != null)
            {
                inputOrder.Remove(record.InputOrderNode);
                record.InputOrderNode = null;
            }

            RemoveOwned(Objects.TryGet(objectId, out ObjectRow row) ? row.OwnerId : 0, record);
            try
            {
                OnUnspawning?.Invoke(record);
                foreach (EntityBehaviour behaviour in entity.EntityBehaviours)
                {
                    behaviour.OnStopServer();
                }
            }
            finally
            {
                if (session.State == ServerState.Started)
                {
                    Objects.Despawn(objectId);
                }

                record.Server = null;
                entity.Unbind(record);
                if (destroy)
                {
                    backend.End(entity, sceneObjects.Contains(entity));
                }
            }

            return true;
        }

        private void CaptureStates(IReadOnlyList<EntityBehaviour> behaviours, bool everySlot)
        {
            foreach (EntityBehaviour behaviour in behaviours)
            {
                if (behaviour.StateSlot != null && (everySlot || behaviour.StateSlot.NeedsCapture))
                {
                    behaviour.PrepareState();
                    behaviour.StateSlot.Capture();
                }
            }
        }

        private void RouteRpcs(IReadOnlyList<EntityBehaviour> behaviours)
        {
            foreach (EntityBehaviour behaviour in behaviours)
            {
                Type type = behaviour.DeclaringType;
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
                    dispatcher.RegisterObject(routed, (peerId, objectId, behaviourIndex, body) => DeliverRpc(routed, peerId, objectId, behaviourIndex, body));
                    rpcTypes.Add(routed, type);
                }
            }
        }

        private void RouteStates(IReadOnlyList<EntityBehaviour> behaviours)
        {
            foreach (EntityBehaviour behaviour in behaviours)
            {
                StateSlot slot = behaviour.StateSlot;
                if (slot == null)
                {
                    continue;
                }

                if (rpcTypes.ContainsKey(slot.MessageId))
                {
                    throw new HandlerRegistrationException($"message id 0x{slot.MessageId:X8} is already a server RPC of {rpcTypes[slot.MessageId].FullName}; {behaviour.DeclaringType.FullName} cannot use it as a state model");
                }

                stateIds.Add(slot.MessageId);
            }
        }

        private void DeliverRpc(uint messageId, ulong peerId, uint objectId, byte behaviourIndex, ReadOnlyMemory<byte> body)
        {
            if (!spawned.TryGetValue(objectId, out EntityRecord record))
            {
                return;
            }

            IReadOnlyList<EntityBehaviour> behaviours = record.Representation.EntityBehaviours;
            if (behaviourIndex >= behaviours.Count || !behaviours[behaviourIndex].TryGetServerRpc(messageId, out ServerRpc rpc))
            {
                return;
            }

            if (rpc.RequireOwnership && record.OwnerId != peerId)
            {
                log.Warning($"Fomoxa dropped server RPC 0x{messageId:X8} to object {objectId} from peer {peerId}, which does not own the object");
                return;
            }

            rpc.Invoke(peerId, body);
        }

        private void Resync(ulong peerId, uint objectId, byte behaviourIndex, ReadOnlyMemory<byte> body)
        {
            if (!spawned.TryGetValue(objectId, out EntityRecord record))
            {
                return;
            }

            IReadOnlyList<EntityBehaviour> behaviours = record.Representation.EntityBehaviours;
            if (behaviourIndex >= behaviours.Count)
            {
                return;
            }

            StateSlot slot = behaviours[behaviourIndex].StateSlot;
            if (slot == null || !Objects.IsObserver(objectId, peerId))
            {
                return;
            }

            session.SendToObject(peerId, slot.MessageId, objectId, behaviourIndex, slot.Sent.Span);
        }

        private void ResyncCollection(ulong peerId, uint objectId, byte behaviourIndex, ReadOnlyMemory<byte> body)
        {
            if (!spawned.TryGetValue(objectId, out EntityRecord record) || !Objects.IsObserver(objectId, peerId))
            {
                return;
            }

            IReadOnlyList<EntityBehaviour> behaviours = record.Representation.EntityBehaviours;
            if (behaviourIndex >= behaviours.Count)
            {
                return;
            }

            CollectionProtocol protocol = stateProtocol.Collections;
            protocol.ResyncCodec.Decode(body, ref collectionResync);
            CollectionSlot slot = behaviours[behaviourIndex].CollectionAt(collectionResync.Collection);
            if (slot == null)
            {
                return;
            }

            session.SendToObject(peerId, protocol.ContentCodec.MessageId, objectId, behaviourIndex, protocol.ContentCodec.Encode(slot.Content()).Span);
        }

        private bool DecideObserver(uint objectId, ulong peerId)
        {
            EntityRecord record = Spawning;
            if (record == null && !spawned.TryGetValue(objectId, out record))
            {
                return false;
            }

            if (record.GroupIds != null && !Groups.Shares(record.GroupIds, peerId))
            {
                return false;
            }

            switch (record.Representation.Visibility)
            {
                case NetworkVisibility.Everyone:
                    return true;
                case NetworkVisibility.OwnerOnly:
                    return false;
            }

            IObserverRule rule = ObserverRule;
            if (rule == null)
            {
                return true;
            }

            try
            {
                return rule.Observes(Observers, record.Representation, peerId);
            }
            catch (Exception exception)
            {
                log.Exception(exception);
                return false;
            }
        }

        private bool CollectCandidates(uint objectId, HashSet<ulong> peers)
        {
            EntityRecord record = Spawning;
            if (record == null && !spawned.TryGetValue(objectId, out record))
            {
                return false;
            }

            if (record.GroupIds == null)
            {
                return false;
            }

            Groups.CollectPeers(record.GroupIds, peers);
            return true;
        }

        private void RebuildForFirstAnchor(ulong peerId)
        {
            IObserverRule rule = ObserverRule;
            if (rule != null && rule.RebuildsOnFirstAnchor && session.PeerState(peerId) == ConnectionState.Started)
            {
                Objects.RebuildObserversOfPeer(peerId);
            }
        }

        private void DespawnForLeavingOwner(uint objectId)
        {
            if (!spawned.TryGetValue(objectId, out EntityRecord record))
            {
                Objects.Despawn(objectId);
                return;
            }

            try
            {
                Unspawn(record, true);
            }
            catch (Exception exception)
            {
                log.Exception(exception);
            }
        }

        private void RaiseOwnerChanged(ObjectOwnerChangedArgs args)
        {
            if (!spawned.TryGetValue(args.ObjectId, out EntityRecord record))
            {
                return;
            }

            RemoveOwned(args.PreviousOwnerId, record);
            AddOwned(args.OwnerId, record);
            bool ownerLeft = args.PreviousOwnerId != 0 && session.PeerState(args.PreviousOwnerId) != ConnectionState.Started;
            try
            {
                foreach (EntityBehaviour behaviour in record.Representation.EntityBehaviours)
                {
                    behaviour.OnOwnerChangedServer(args.PreviousOwnerId);
                }
            }
            catch (Exception exception) when (ownerLeft)
            {
                log.Exception(exception);
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
                    log.Exception(exception);
                }
            }

            sceneObjects.Clear();
            owned.Clear();
            anchors.Clear();
            Groups.Clear();
            queuedStates.Clear();
        }

        private void ForgetLeavingPeer(ConnectionStateArgs args)
        {
            if (args.State != ConnectionState.Stopped)
            {
                return;
            }

            anchors.Remove(args.PeerId);
            Groups.ForgetPeer(args.PeerId);
            if (owned.TryGetValue(args.PeerId, out List<EntityRecord> records) && records.Count == 0)
            {
                owned.Remove(args.PeerId);
            }
        }

        private bool AddOwned(ulong ownerId, EntityRecord record)
        {
            if (ownerId == 0)
            {
                return false;
            }

            if (!owned.TryGetValue(ownerId, out List<EntityRecord> records))
            {
                records = new List<EntityRecord>();
                owned.Add(ownerId, records);
            }

            records.Add(record);
            return records.Count == 1;
        }

        private void RemoveOwned(ulong ownerId, EntityRecord record)
        {
            if (ownerId != 0 && owned.TryGetValue(ownerId, out List<EntityRecord> records))
            {
                records.Remove(record);
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

        private static void CaptureTransforms(EntityRecord record, IReadOnlyList<EntityBehaviour> behaviours)
        {
            foreach (EntityBehaviour behaviour in behaviours)
            {
                TransformSlot slot = behaviour.TransformSlot;
                if (slot != null)
                {
                    slot.Record = record;
                    slot.Source.ReadLocal(out Vector3 position, out Quaternion rotation, out Vector3 scale);
                    slot.Sync.CaptureSpawn(position, rotation, scale);
                }
            }
        }

        private ReadOnlySpan<byte> EncodeSettle(TransformSync sync, uint tick, byte mask, in Vector3 position, in Quaternion rotation, in Vector3 scale)
        {
            transformSettle.Tick = tick;
            transformSettle.Mask = mask;
            transformSettle.Generation = sync.Generation;
            SpawnTransform.PackSelected(mask, position, rotation, scale, transformSettle.Values);
            return transformProtocol.SettleCodec.Encode(transformSettle).Span;
        }

        private void LogRejectedInput(InputRejectedArgs args)
        {
            log.Warning($"Fomoxa dropped input to object {args.ObjectId} from peer {args.PeerId}, which does not own the object");
        }

        private readonly struct ReconcileSend
        {
            public ReconcileSend(EntityRecord record, byte behaviourIndex, ulong ownerId)
            {
                Record = record;
                BehaviourIndex = behaviourIndex;
                OwnerId = ownerId;
            }

            public EntityRecord Record { get; }

            public byte BehaviourIndex { get; }

            public ulong OwnerId { get; }
        }

        private sealed class AnchorPositions
        {
            public readonly List<Vector3> Positions = new List<Vector3>();

            public int Generation;
        }
    }
}
