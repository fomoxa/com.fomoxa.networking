using System;
using System.Collections.Generic;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Prediction
{
    internal sealed class ClientPrediction
    {
        private readonly ClientEntities entities;
        private readonly ClientSession session;
        private readonly InputProtocol inputProtocol;
        private readonly IClientPredictionBackend backend;
        private readonly NetworkLog log;
        private readonly InputFrames inputFrames = new InputFrames();
        private readonly List<EntityRecord> gathering = new List<EntityRecord>();
        private readonly List<EntityRecord> predicted = new List<EntityRecord>();
        private readonly List<EntityRecord> worldless = new List<EntityRecord>();
        private readonly List<EntityRecord> groupRecords = new List<EntityRecord>();
        private readonly List<IPhysicsSimulation> entityWorlds = new List<IPhysicsSimulation>();
        private readonly List<IPhysicsSimulation> predictedWorldsOf = new List<IPhysicsSimulation>();
        private readonly List<int> worldStarts = new List<int>();
        private readonly List<IPhysicsSimulation> predictedWorlds = new List<IPhysicsSimulation>();
        private readonly ReplayGroups replayGroups = new ReplayGroups();
        private readonly List<PhysicsHistory> replayHistories = new List<PhysicsHistory>();
        private readonly List<HeldState> heldStates = new List<HeldState>();
        private ReconcileState reconcileState = new ReconcileState();

        public ClientPrediction(ClientEntities entities, ClientSession session, MessageDispatcher dispatcher, InputProtocol inputProtocol, IClientPredictionBackend backend, NetworkLog log)
        {
            this.entities = entities;
            this.session = session;
            this.inputProtocol = inputProtocol;
            this.backend = backend;
            this.log = log;
            dispatcher.RegisterObject(inputProtocol.ReconcileCodec.MessageId, (peerId, objectId, behaviourIndex, body) => HoldReconcileState(objectId, behaviourIndex, body));
            entities.OnDespawning += record => backend.EndProxy(record.Representation);
        }

        public bool IsReplaying { get; private set; }

        public bool SimulatesPhysics { get; set; }

        public void PlaceProxies()
        {
            if (entities.ConnectedLocally || session.State != ConnectionState.Started)
            {
                return;
            }

            backend.ForgetDestroyedProxies();
            foreach (EntityRecord record in entities.Spawned.Values)
            {
                if (ClientEntities.IsPredicting(record.Representation.EntityBehaviours))
                {
                    backend.EndProxy(record.Representation);
                }
                else
                {
                    backend.PlaceProxy(record.Representation);
                }
            }
        }

        public void EndProxies()
        {
            foreach (EntityRecord record in entities.Spawned.Values)
            {
                backend.EndProxy(record.Representation);
            }
        }

        public void Predict(uint predictionTick, bool clockSynced, float tickSeconds)
        {
            predicted.Clear();
            if (entities.ConnectedLocally || session.State != ConnectionState.Started || !clockSynced)
            {
                return;
            }

            gathering.Clear();
            foreach (EntityRecord record in entities.Spawned.Values)
            {
                if (record.HasInput)
                {
                    gathering.Add(record);
                }
            }

            foreach (EntityRecord record in gathering)
            {
                if (entities.Objects.IsOwner(record.ObjectId))
                {
                    predicted.Add(record);
                }
                else
                {
                    StopPredicting(record);
                }
            }

            gathering.Clear();
            if (SimulatesPhysics)
            {
                ReconcileWorlds(tickSeconds);
            }
            else
            {
                foreach (EntityRecord record in predicted)
                {
                    Reconcile(record);
                }
            }

            foreach (EntityRecord record in predicted)
            {
                PredictOwned(record, predictionTick);
            }
        }

        public void CapturePredicted(uint predictionTick)
        {
            if (!SimulatesPhysics)
            {
                predicted.Clear();
                return;
            }

            predictedWorlds.Clear();
            foreach (EntityRecord record in predicted)
            {
                if (record.Client != entities)
                {
                    continue;
                }

                bool capturedAny = false;
                foreach (EntityBehaviour behaviour in record.Representation.EntityBehaviours)
                {
                    InputSlot slot = behaviour.InputSlot;
                    if (slot?.Reconcile != null && slot.Predicting)
                    {
                        capturedAny = true;
                        CaptureAt(slot, predictionTick);
                    }
                }

                if (!capturedAny)
                {
                    continue;
                }

                entityWorlds.Clear();
                backend.WorldsOf(record.Representation, entityWorlds);
                foreach (IPhysicsSimulation world in entityWorlds)
                {
                    if (!predictedWorlds.Contains(world))
                    {
                        predictedWorlds.Add(world);
                    }
                }
            }

            foreach (IPhysicsSimulation world in predictedWorlds)
            {
                backend.HistoryOf(world, entities.InputRules.History).Save(predictionTick);
            }

            entityWorlds.Clear();
            predictedWorlds.Clear();
            predicted.Clear();
        }

        private void HoldReconcileState(uint objectId, byte behaviourIndex, ReadOnlyMemory<byte> body)
        {
            if (entities.ConnectedLocally
                || !entities.TryGet(objectId, out EntityRecord record)
                || !entities.Objects.IsOwner(objectId))
            {
                return;
            }

            IReadOnlyList<EntityBehaviour> behaviours = record.Representation.EntityBehaviours;
            if (behaviourIndex >= behaviours.Count)
            {
                return;
            }

            InputSlot slot = behaviours[behaviourIndex].InputSlot;
            if (slot?.Reconcile == null)
            {
                return;
            }

            inputProtocol.ReconcileCodec.Decode(body, ref reconcileState);
            slot.HoldPending(reconcileState.Tick, reconcileState.Data.Span);
        }

        private void Reconcile(EntityRecord record)
        {
            IReadOnlyList<EntityBehaviour> behaviours = record.Representation.EntityBehaviours;
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
                    backend.BeginCorrection(record.Representation);
                }

                if (!slot.Predicting)
                {
                    ClientEntities.ResetReceive(behaviours);
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
                    log.Exception(exception);
                }
            }

            if (replayedAny)
            {
                backend.EndCorrection(record.Representation);
            }
        }

        private void ReconcileWorlds(float tickSeconds)
        {
            replayGroups.Clear();
            worldless.Clear();
            predictedWorldsOf.Clear();
            worldStarts.Clear();
            foreach (EntityRecord record in predicted)
            {
                worldStarts.Add(predictedWorldsOf.Count);
                backend.WorldsOf(record.Representation, predictedWorldsOf);
            }

            worldStarts.Add(predictedWorldsOf.Count);
            for (int index = 0; index < predicted.Count; index++)
            {
                int start = worldStarts[index];
                int count = worldStarts[index + 1] - start;
                if (count == 0)
                {
                    worldless.Add(predicted[index]);
                    continue;
                }

                entityWorlds.Clear();
                for (int offset = 0; offset < count; offset++)
                {
                    entityWorlds.Add(predictedWorldsOf[start + offset]);
                }

                replayGroups.Add(entityWorlds);
            }

            foreach (ReplayGroup group in replayGroups.Groups)
            {
                groupRecords.Clear();
                for (int index = 0; index < predicted.Count; index++)
                {
                    int start = worldStarts[index];
                    int count = worldStarts[index + 1] - start;
                    if (count > 0 && group.HoldsAny(predictedWorldsOf, start, count))
                    {
                        groupRecords.Add(predicted[index]);
                    }
                }

                ReconcileGroup(group, groupRecords, tickSeconds);
            }

            foreach (EntityRecord record in worldless)
            {
                Reconcile(record);
            }

            entityWorlds.Clear();
            predictedWorldsOf.Clear();
            worldStarts.Clear();
            groupRecords.Clear();
            worldless.Clear();
            replayGroups.Clear();
        }

        private void ReconcileGroup(ReplayGroup group, List<EntityRecord> records, float tickSeconds)
        {
            if (!TryFindNewestPending(records, out uint target))
            {
                return;
            }

            heldStates.Clear();
            bool mismatched = false;
            foreach (EntityRecord record in records)
            {
                IReadOnlyList<EntityBehaviour> behaviours = record.Representation.EntityBehaviours;
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
                        log.Exception(exception);
                    }

                    heldStates.Add(new HeldState(record, behaviours[index], server, matched));
                    mismatched |= !matched;
                }
            }

            if (mismatched)
            {
                Replay(group, records, target, tickSeconds);
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

        private void Replay(ReplayGroup group, List<EntityRecord> records, uint target, float tickSeconds)
        {
            foreach (EntityRecord record in records)
            {
                backend.BeginCorrection(record.Representation);
            }

            foreach (HeldState held in heldStates)
            {
                if (!held.Behaviour.InputSlot.Predicting)
                {
                    ClientEntities.ResetReceive(held.Record.Representation.EntityBehaviours);
                    backend.EndProxy(held.Record.Representation);
                }
            }

            IsReplaying = true;
            bool loaded = LoadGroup(group, target);
            if (loaded)
            {
                foreach (IPhysicsSimulation world in group.Worlds)
                {
                    backend.TrackerOf(world)?.Restore(target);
                }
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
                    log.Exception(exception);
                }

                slot.Predicting = true;
            }

            uint newest = NewestPredictedTick(records, target);
            int capacity = entities.InputRules.History;
            for (uint tick = target + 1; tick <= newest && tick > target; tick++)
            {
                foreach (EntityRecord record in records)
                {
                    foreach (EntityBehaviour behaviour in record.Representation.EntityBehaviours)
                    {
                        InputSlot slot = behaviour.InputSlot;
                        if (slot?.Reconcile != null && slot.Predicting)
                        {
                            ReplayInput(slot, tick);
                        }
                    }
                }

                foreach (IPhysicsSimulation world in group.Worlds)
                {
                    world.Step(tickSeconds);
                }

                foreach (IPhysicsSimulation world in group.Worlds)
                {
                    IContactTracker tracker = backend.TrackerOf(world);
                    tracker?.Query();
                    if (loaded)
                    {
                        tracker?.Record(tick, capacity);
                    }
                }

                foreach (EntityRecord record in records)
                {
                    foreach (EntityBehaviour behaviour in record.Representation.EntityBehaviours)
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
            foreach (IPhysicsSimulation world in group.Worlds)
            {
                backend.TrackerOf(world)?.Publish();
            }

            foreach (EntityRecord record in records)
            {
                backend.EndCorrection(record.Representation);
            }
        }

        private bool LoadGroup(ReplayGroup group, uint target)
        {
            replayHistories.Clear();
            foreach (IPhysicsSimulation world in group.Worlds)
            {
                replayHistories.Add(backend.HistoryOf(world, entities.InputRules.History));
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

        private static bool TryFindNewestPending(List<EntityRecord> records, out uint target)
        {
            target = 0;
            bool found = false;
            foreach (EntityRecord record in records)
            {
                foreach (EntityBehaviour behaviour in record.Representation.EntityBehaviours)
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

        private static uint NewestPredictedTick(List<EntityRecord> records, uint target)
        {
            uint newest = target;
            foreach (EntityRecord record in records)
            {
                foreach (EntityBehaviour behaviour in record.Representation.EntityBehaviours)
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

        private void ReplayInput(InputSlot slot, uint tick)
        {
            try
            {
                slot.ReplayInput(tick);
            }
            catch (Exception exception)
            {
                log.Exception(exception);
            }
        }

        private void CaptureAt(InputSlot slot, uint tick)
        {
            try
            {
                slot.CaptureAt(tick);
            }
            catch (Exception exception)
            {
                log.Exception(exception);
            }
        }

        private void PredictOwned(EntityRecord record, uint predictionTick)
        {
            IReadOnlyList<EntityBehaviour> behaviours = record.Representation.EntityBehaviours;
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
                    log.Exception(exception);
                    continue;
                }

                inputFrames.Tick = predictionTick;
                slot.CopyFramesTo(inputFrames.Frames);
                session.SendToObject(inputProtocol.FramesCodec.MessageId, record.ObjectId, (byte)index, inputProtocol.FramesCodec.Encode(inputFrames).Span);
            }
        }

        private static void StopPredicting(EntityRecord record)
        {
            IReadOnlyList<EntityBehaviour> behaviours = record.Representation.EntityBehaviours;
            bool wasPredicting = ClientEntities.IsPredicting(behaviours);
            foreach (EntityBehaviour behaviour in behaviours)
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
                ClientEntities.ResetReceive(behaviours);
            }
        }

        private readonly struct HeldState
        {
            public HeldState(EntityRecord record, EntityBehaviour behaviour, ReadOnlyMemory<byte> server, bool matched)
            {
                Record = record;
                Behaviour = behaviour;
                Server = server;
                Matched = matched;
            }

            public EntityRecord Record { get; }

            public EntityBehaviour Behaviour { get; }

            public ReadOnlyMemory<byte> Server { get; }

            public bool Matched { get; }
        }
    }
}
