using System;
using System.Collections.Generic;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Prediction;
using Fomoxa.Networking.Sessions;

namespace Fomoxa.Networking.Objects
{
    internal sealed class ServerEntities
    {
        private readonly ServerSession session;
        private readonly MessageDispatcher dispatcher;
        private readonly MessageChannels channels;
        private readonly StateProtocol stateProtocol;
        private readonly InputProtocol inputProtocol;
        private readonly RpcMessageIds rpcIds;
        private readonly IServerEntityBackend backend;
        private readonly NetworkLog log;
        private readonly Dictionary<uint, EntityRecord> spawned = new Dictionary<uint, EntityRecord>();
        private readonly LinkedList<EntityRecord> spawnOrder = new LinkedList<EntityRecord>();
        private readonly HashSet<INetworkEntity> sceneObjects = new HashSet<INetworkEntity>();
        private readonly Dictionary<ulong, List<EntityRecord>> owned = new Dictionary<ulong, List<EntityRecord>>();
        private readonly Dictionary<uint, Type> rpcTypes = new Dictionary<uint, Type>();
        private readonly HashSet<uint> stateIds = new HashSet<uint>();
        private readonly StateDelta stateDelta = new StateDelta();
        private readonly ReconcileState reconcileState = new ReconcileState();
        private readonly List<EntityRecord> applying = new List<EntityRecord>();
        private readonly List<ReconcileSend> reconciling = new List<ReconcileSend>();
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
            this.rpcIds = rpcIds;
            this.backend = backend;
            this.log = log;
            Representations = new RepresentationTable(spawned);
            dispatcher.RegisterObject(stateProtocol.ResyncCodec.MessageId, Resync);
            inputs.OnInputRejected += LogRejectedInput;
            objects.Observes = DecideObserver;
            objects.OnOwnerChanged += RaiseOwnerChanged;
            objects.DespawnWithOwner = objectId => !spawned.TryGetValue(objectId, out EntityRecord record) || record.Representation.DespawnWithOwner;
            objects.Despawner = DespawnForLeavingOwner;
            session.OnRemoteConnectionState += ForgetLeavingPeer;
            session.OnServerConnectionState += DespawnAllWhenStopped;
        }

        public event Action<EntityRecord> OnSpawned;

        public event Action<EntityRecord> OnUnspawning;

        public event Action<ulong> OnFirstOwned;

        public object Owner { get; }

        public ServerObjects Objects { get; }

        public ServerInputs Inputs { get; }

        public InputRules InputRules { get; set; } = new InputRules();

        public Func<EntityRecord, ulong, bool> ObserverRule { get; set; }

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
            foreach (EntityBehaviour behaviour in behaviours)
            {
                behaviour.Register(rpcIds, channels, stateProtocol, InputRules);
                record.HasInput |= behaviour.InputSlot != null;
            }

            RouteRpcs(behaviours);
            RouteStates(behaviours);
            CaptureStates(behaviours);
            uint sceneId = backend.SceneIdOf(entity);
            uint objectId;
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
                    OnFirstOwned?.Invoke(ownerId);
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

            ulong previousOwnerId = record.OwnerId;
            if (!Objects.ChangeOwner(record.ObjectId, ownerId))
            {
                return false;
            }

            if (previousOwnerId != ownerId && owned.TryGetValue(ownerId, out List<EntityRecord> records) && records.Count == 1)
            {
                OnFirstOwned?.Invoke(ownerId);
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

            EntityRecord record = entity.Record;
            return record != null && record.Server == this && Objects.RebuildObservers(record.ObjectId);
        }

        public void SyncStates()
        {
            foreach (EntityRecord record in spawnOrder)
            {
                IReadOnlyList<EntityBehaviour> behaviours = record.Representation.EntityBehaviours;
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

                    Objects.SendToObservers(messageId, record.ObjectId, (byte)index, body);
                }
            }
        }

        public void ApplyInputs(uint tick, ulong hostPeerId)
        {
            applying.Clear();
            foreach (EntityRecord record in spawnOrder)
            {
                if (record.HasInput)
                {
                    applying.Add(record);
                }
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

        private void CaptureStates(IReadOnlyList<EntityBehaviour> behaviours)
        {
            foreach (EntityBehaviour behaviour in behaviours)
            {
                if (behaviour.StateSlot != null)
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

        private bool DecideObserver(uint objectId, ulong peerId)
        {
            EntityRecord record = Spawning;
            if (record == null && !spawned.TryGetValue(objectId, out record))
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

            Func<EntityRecord, ulong, bool> rule = ObserverRule;
            if (rule == null)
            {
                return true;
            }

            try
            {
                return rule(record, peerId);
            }
            catch (Exception exception)
            {
                log.Exception(exception);
                return false;
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
        }

        private void ForgetLeavingPeer(ConnectionStateArgs args)
        {
            if (args.State == ConnectionState.Stopped && owned.TryGetValue(args.PeerId, out List<EntityRecord> records) && records.Count == 0)
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
    }
}
