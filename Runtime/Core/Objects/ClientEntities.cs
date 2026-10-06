using System;
using System.Collections.Generic;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;

namespace Fomoxa.Networking.Objects
{
    internal sealed class ClientEntities : IObjectSpawner
    {
        private readonly ClientSession session;
        private readonly MessageDispatcher dispatcher;
        private readonly MessageChannels channels;
        private readonly StateProtocol stateProtocol;
        private readonly RpcMessageIds rpcIds;
        private readonly ServerEntities hostServer;
        private readonly IClientEntityBackend backend;
        private readonly NetworkLog log;
        private readonly Dictionary<uint, EntityRecord> spawned = new Dictionary<uint, EntityRecord>();
        private readonly Dictionary<uint, Type> rpcTypes = new Dictionary<uint, Type>();
        private readonly HashSet<uint> stateIds = new HashSet<uint>();
        private readonly List<EntityRecord> hiddenStarted = new List<EntityRecord>();
        private StateDelta stateDelta = new StateDelta();

        public ClientEntities(
            object owner,
            ClientSession session,
            MessageDispatcher dispatcher,
            MessageChannels channels,
            StateProtocol stateProtocol,
            RpcMessageIds rpcIds,
            ServerEntities hostServer,
            IClientEntityBackend backend,
            NetworkLog log)
        {
            Owner = owner;
            this.session = session;
            this.dispatcher = dispatcher;
            this.channels = channels;
            this.stateProtocol = stateProtocol;
            this.rpcIds = rpcIds;
            this.hostServer = hostServer;
            this.backend = backend;
            this.log = log;
            dispatcher.RegisterObject(stateProtocol.DeltaCodec.MessageId, (peerId, objectId, behaviourIndex, body) => DeliverDelta(objectId, behaviourIndex, body));
        }

        public event Action<EntityRecord> OnDespawning;

        public object Owner { get; }

        public ClientObjects Objects { get; private set; }

        public bool ConnectedLocally { get; set; }

        public InputRules InputRules { get; set; } = new InputRules();

        public Dictionary<uint, EntityRecord> Spawned => spawned;

        public bool TryGet(uint objectId, out EntityRecord record) => spawned.TryGetValue(objectId, out record);

        public void Attach(ClientObjects objects)
        {
            Objects = objects;
            objects.OnOwnerChanged += RaiseOwnerChanged;
        }

        public SpawnResult Spawn(in SpawnedObject spawnedObject)
        {
            bool isSceneObject = spawnedObject.SceneObjectId != 0;
            if (!isSceneObject)
            {
                SpawnResult checkedPrefab = backend.CheckPrefab(spawnedObject);
                if (checkedPrefab != SpawnResult.Spawned)
                {
                    return checkedPrefab;
                }
            }

            uint objectId = spawnedObject.ObjectId;
            EntityRecord record;
            INetworkEntity entity;
            if (ConnectedLocally)
            {
                if (hostServer == null || !hostServer.TryGet(objectId, out record))
                {
                    return SpawnResult.Spawned;
                }

                entity = record.Representation;
                if (spawned.TryGetValue(objectId, out EntityRecord hidden) && hidden == record)
                {
                    if (!TryApplyStates(record.Representation.EntityBehaviours, spawnedObject.States, true))
                    {
                        return SpawnResult.InvalidSpawn;
                    }

                    backend.ShowOnHost(entity);
                    return SpawnResult.Spawned;
                }
            }
            else
            {
                if (isSceneObject)
                {
                    SpawnResult placed = backend.PlaceSceneObject(spawnedObject, out entity);
                    if (placed != SpawnResult.Spawned)
                    {
                        return placed;
                    }
                }
                else
                {
                    entity = backend.Create(spawnedObject);
                }

                record = new EntityRecord(entity, spawnedObject.PrefabFingerprint) { ObjectId = objectId };
                entity.Bind(record);
            }

            record.Client = this;
            spawned.Add(objectId, record);
            bool statesValid;
            try
            {
                foreach (EntityBehaviour behaviour in entity.EntityBehaviours)
                {
                    behaviour.Register(rpcIds, channels, stateProtocol, InputRules);
                    record.HasInput |= behaviour.InputSlot != null;
                }

                statesValid = TryApplyStates(record.Representation.EntityBehaviours, spawnedObject.States, record.Server != null);
                if (statesValid)
                {
                    backend.PrepareReceive(entity);
                    RouteRpcs(entity.EntityBehaviours);
                    RouteStates(entity.EntityBehaviours);
                    foreach (EntityBehaviour behaviour in entity.EntityBehaviours)
                    {
                        behaviour.OnStartClient();
                    }
                }
            }
            catch
            {
                Abandon(record);
                throw;
            }

            if (!statesValid)
            {
                Abandon(record);
                return SpawnResult.InvalidSpawn;
            }

            if (record.Server != null)
            {
                backend.ShowOnHost(entity);
            }

            return SpawnResult.Spawned;
        }

        public void Despawn(uint objectId)
        {
            if (session.State == ConnectionState.Started && spawned.TryGetValue(objectId, out EntityRecord shared) && shared.Server != null)
            {
                backend.HideOnHost(shared.Representation);
                return;
            }

            if (!spawned.Remove(objectId, out EntityRecord record))
            {
                return;
            }

            OnDespawning?.Invoke(record);
            try
            {
                foreach (EntityBehaviour behaviour in record.Representation.EntityBehaviours)
                {
                    behaviour.OnStopClient();
                }
            }
            catch (Exception exception) when (session.State == ConnectionState.Stopped)
            {
                log.Exception(exception);
            }
            finally
            {
                Detach(record);
            }
        }

        public void EndShared(EntityRecord record)
        {
            if (record.Client != this || !spawned.Remove(record.ObjectId))
            {
                return;
            }

            try
            {
                foreach (EntityBehaviour behaviour in record.Representation.EntityBehaviours)
                {
                    behaviour.OnStopClient();
                }
            }
            finally
            {
                record.Client = null;
            }
        }

        public bool ForgetDestroyed(INetworkEntity entity)
        {
            EntityRecord record = entity.Record;
            if (record == null || record.Client != this || !spawned.Remove(record.ObjectId))
            {
                return false;
            }

            log.Warning($"Fomoxa: object {record.ObjectId} (prefab 0x{entity.PrefabId:X8}) was destroyed on the client without a despawn from the server");
            record.Client = null;
            if (record.Server == null)
            {
                entity.Unbind(record);
            }

            return true;
        }

        public void DespawnHiddenOnHost()
        {
            hiddenStarted.Clear();
            foreach (EntityRecord record in spawned.Values)
            {
                if (!Objects.TryGet(record.ObjectId, out _))
                {
                    hiddenStarted.Add(record);
                }
            }

            for (int index = hiddenStarted.Count - 1; index >= 0; index--)
            {
                Despawn(hiddenStarted[index].ObjectId);
            }

            hiddenStarted.Clear();
        }

        private void Abandon(EntityRecord record)
        {
            spawned.Remove(record.ObjectId);
            Detach(record);
        }

        private void Detach(EntityRecord record)
        {
            record.Client = null;
            if (record.Server == null)
            {
                INetworkEntity entity = record.Representation;
                entity.Unbind(record);
                backend.End(entity);
            }
        }

        internal static bool TryApplyStates(IReadOnlyList<EntityBehaviour> behaviours, IReadOnlyList<ReadOnlyMemory<byte>> states, bool shared)
        {
            if (states.Count != behaviours.Count)
            {
                return false;
            }

            for (int index = 0; index < behaviours.Count; index++)
            {
                StateSlot slot = behaviours[index].StateSlot;
                if (slot == null || states[index].IsEmpty)
                {
                    if (slot != null || !states[index].IsEmpty)
                    {
                        return false;
                    }

                    continue;
                }

                try
                {
                    slot.Validate(states[index]);
                }
                catch (MessageDecodeException)
                {
                    return false;
                }
            }

            for (int index = 0; index < behaviours.Count; index++)
            {
                behaviours[index].StateSlot?.ApplyInitial(states[index], shared);
            }

            return true;
        }

        private void RouteRpcs(IReadOnlyList<EntityBehaviour> behaviours)
        {
            foreach (EntityBehaviour behaviour in behaviours)
            {
                Type type = behaviour.DeclaringType;
                foreach (uint messageId in behaviour.ClientRpcIds)
                {
                    if (stateIds.Contains(messageId))
                    {
                        throw new HandlerRegistrationException($"message id 0x{messageId:X8} is already a state model; {type.FullName} cannot use it as a client RPC");
                    }

                    if (rpcTypes.TryGetValue(messageId, out Type existing))
                    {
                        if (existing != type)
                        {
                            throw new HandlerRegistrationException($"message id 0x{messageId:X8} is already a client RPC of {existing.FullName}; {type.FullName} cannot use it");
                        }

                        continue;
                    }

                    uint routed = messageId;
                    dispatcher.RegisterObject(routed, (peerId, objectId, behaviourIndex, body) => DeliverRpc(routed, objectId, behaviourIndex, body));
                    rpcTypes.Add(routed, type);
                }
            }
        }

        private void RouteStates(IReadOnlyList<EntityBehaviour> behaviours)
        {
            foreach (EntityBehaviour behaviour in behaviours)
            {
                StateSlot slot = behaviour.StateSlot;
                if (slot == null || stateIds.Contains(slot.MessageId))
                {
                    continue;
                }

                if (rpcTypes.ContainsKey(slot.MessageId))
                {
                    throw new HandlerRegistrationException($"message id 0x{slot.MessageId:X8} is already a client RPC of {rpcTypes[slot.MessageId].FullName}; {behaviour.DeclaringType.FullName} cannot use it as a state model");
                }

                uint routed = slot.MessageId;
                dispatcher.RegisterObject(routed, (peerId, objectId, behaviourIndex, body) => DeliverState(routed, objectId, behaviourIndex, body));
                stateIds.Add(routed);
            }
        }

        private void DeliverState(uint messageId, uint objectId, byte behaviourIndex, ReadOnlyMemory<byte> body)
        {
            if (!TryGetBehaviour(objectId, behaviourIndex, out EntityRecord record, out EntityBehaviour behaviour))
            {
                return;
            }

            StateSlot slot = behaviour.StateSlot;
            if (slot == null || slot.MessageId != messageId)
            {
                return;
            }

            try
            {
                slot.Receive(body, record.Server != null);
            }
            catch (MessageDecodeException)
            {
                LoseSync(slot, objectId, behaviourIndex);
                throw;
            }
        }

        private void DeliverDelta(uint objectId, byte behaviourIndex, ReadOnlyMemory<byte> body)
        {
            if (!TryGetBehaviour(objectId, behaviourIndex, out EntityRecord record, out EntityBehaviour behaviour))
            {
                return;
            }

            StateSlot slot = behaviour.StateSlot;
            if (slot == null || slot.OutOfSync)
            {
                return;
            }

            try
            {
                stateProtocol.DeltaCodec.Decode(body, ref stateDelta);
                slot.ReceiveDelta(stateDelta.Data, record.Server != null);
            }
            catch (MessageDecodeException)
            {
                LoseSync(slot, objectId, behaviourIndex);
                throw;
            }
        }

        private void DeliverRpc(uint messageId, uint objectId, byte behaviourIndex, ReadOnlyMemory<byte> body)
        {
            if (TryGetBehaviour(objectId, behaviourIndex, out EntityRecord _, out EntityBehaviour behaviour)
                && behaviour.TryGetClientRpc(messageId, out ClientRpc rpc))
            {
                rpc.Invoke(body);
            }
        }

        private void LoseSync(StateSlot slot, uint objectId, byte behaviourIndex)
        {
            slot.MarkOutOfSync();
            session.SendToObject(stateProtocol.ResyncCodec.MessageId, objectId, behaviourIndex, ReadOnlySpan<byte>.Empty);
        }

        private void RaiseOwnerChanged(ObjectOwnerChangedArgs args)
        {
            if (!spawned.TryGetValue(args.ObjectId, out EntityRecord record))
            {
                return;
            }

            foreach (EntityBehaviour behaviour in record.Representation.EntityBehaviours)
            {
                behaviour.OnOwnerChangedClient(args.PreviousOwnerId);
            }
        }

        private bool TryGetBehaviour(uint objectId, byte behaviourIndex, out EntityRecord record, out EntityBehaviour behaviour)
        {
            if (spawned.TryGetValue(objectId, out record))
            {
                IReadOnlyList<EntityBehaviour> behaviours = record.Representation.EntityBehaviours;
                if (behaviourIndex < behaviours.Count)
                {
                    behaviour = behaviours[behaviourIndex];
                    return true;
                }
            }

            behaviour = null;
            return false;
        }
    }
}
