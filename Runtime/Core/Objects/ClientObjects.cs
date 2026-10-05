using System;
using System.Collections.Generic;
using System.Numerics;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Objects
{
    public sealed class ClientObjects
    {
        private readonly ClientSession session;
        private readonly ObjectProtocol protocol;
        private readonly IObjectSpawner spawner;
        private readonly ObjectTable table = new ObjectTable();
        private readonly HashSet<ulong> sceneObjectIds = new HashSet<ulong>();
        private readonly List<uint> clearing = new List<uint>();
        private LocalPeer localPeer = new LocalPeer();
        private ObjectSpawn spawn = new ObjectSpawn();
        private ObjectSceneSpawn sceneSpawn = new ObjectSceneSpawn();
        private ObjectDespawn despawn = new ObjectDespawn();
        private ObjectOwnerChange ownerChange = new ObjectOwnerChange();
        private readonly SceneProtocol sceneProtocol;
        private readonly ISceneHost sceneHost;
        private readonly HashSet<uint> loadedScenes = new HashSet<uint>();
        private readonly List<SceneStep> sceneSteps = new List<SceneStep>();
        private readonly SceneLoaded sceneLoaded = new SceneLoaded();
        private SceneLoad sceneLoad = new SceneLoad();
        private SceneUnload sceneUnload = new SceneUnload();
        private bool sceneBusy;
        private SceneStep sceneRunning;

        public ClientObjects(ClientSession session, ObjectProtocol protocol, IObjectSpawner spawner)
            : this(session, protocol, spawner, null, null)
        {
        }

        public ClientObjects(ClientSession session, ObjectProtocol protocol, IObjectSpawner spawner, SceneProtocol sceneProtocol, ISceneHost sceneHost)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
            this.spawner = spawner ?? throw new ArgumentNullException(nameof(spawner));
            if ((sceneProtocol == null) != (sceneHost == null))
            {
                throw new ArgumentException("a scene protocol needs a scene host, and a scene host needs a scene protocol", nameof(sceneHost));
            }

            this.sceneProtocol = sceneProtocol;
            this.sceneHost = sceneHost;
            MessageDispatcher dispatcher = session.Dispatcher;
            dispatcher.Register(protocol.LocalPeerCodec.MessageId, OnLocalPeer);
            dispatcher.Register(protocol.SpawnCodec.MessageId, OnSpawn);
            dispatcher.Register(protocol.SceneSpawnCodec.MessageId, OnSceneSpawn);
            dispatcher.Register(protocol.DespawnCodec.MessageId, OnDespawn);
            dispatcher.Register(protocol.OwnerChangeCodec.MessageId, OnOwnerChange);
            if (sceneProtocol != null)
            {
                dispatcher.Register(sceneProtocol.LoadCodec.MessageId, OnSceneLoad);
                dispatcher.Register(sceneProtocol.UnloadCodec.MessageId, OnSceneUnload);
            }

            session.SetStoppedHandler(Clear);
        }

        public event Action<ObjectMismatchArgs> OnObjectMismatch;

        public event Action<ObjectOwnerChangedArgs> OnOwnerChanged;

        public event Action<ulong> OnLocalPeerAssigned;

        public ulong LocalPeerId { get; private set; }

        public ushort ServerTickRate { get; private set; }

        public PhysicsBackend PhysicsBackend { get; set; } = PhysicsBackend.Rigidbody;

        public int Count => table.Count;

        public bool TryGet(uint objectId, out ObjectRow row) => table.TryGet(objectId, out row);

        public IReadOnlyCollection<uint> LoadedScenes => loadedScenes;

        public bool IsOwner(uint objectId) =>
            LocalPeerId != 0 && table.TryGet(objectId, out ObjectRow row) && row.OwnerId == LocalPeerId;

        public void ForgetLoadedScenes()
        {
            loadedScenes.Clear();
        }

        private void OnLocalPeer(ulong peerId, ReadOnlyMemory<byte> payload)
        {
            if (!TryDecode(protocol.LocalPeerCodec, payload, ref localPeer))
            {
                return;
            }

            if (localPeer.TickRate == 0 || !Enum.IsDefined(typeof(PhysicsBackend), localPeer.PhysicsBackend))
            {
                Mismatch(ObjectMismatchKind.InvalidMessage, 0, 0, 0);
                return;
            }

            if ((PhysicsBackend)localPeer.PhysicsBackend != PhysicsBackend)
            {
                session.Abort(StopReason.PhysicsBackendMismatch);
                return;
            }

            LocalPeerId = localPeer.PeerId;
            ServerTickRate = localPeer.TickRate;
            OnLocalPeerAssigned?.Invoke(LocalPeerId);
        }

        private void OnSpawn(ulong peerId, ReadOnlyMemory<byte> payload)
        {
            if (!TryDecode(protocol.SpawnCodec, payload, ref spawn))
            {
                return;
            }

            if (spawn.PrefabId == 0)
            {
                Mismatch(ObjectMismatchKind.InvalidSpawn, spawn.ObjectId, 0, 0);
                return;
            }

            if (spawn.SceneId != 0 && !loadedScenes.Contains(spawn.SceneId))
            {
                Mismatch(ObjectMismatchKind.InvalidSpawn, spawn.ObjectId, spawn.PrefabId, 0);
                return;
            }

            Spawn(spawn.ObjectId, spawn.PrefabId, 0, spawn.PrefabFingerprint, spawn.OwnerId, spawn.Mask, spawn.Values, spawn.States, spawn.SceneId);
        }

        private void OnSceneSpawn(ulong peerId, ReadOnlyMemory<byte> payload)
        {
            if (!TryDecode(protocol.SceneSpawnCodec, payload, ref sceneSpawn))
            {
                return;
            }

            if (sceneSpawn.SceneObjectId == 0)
            {
                Mismatch(ObjectMismatchKind.InvalidSpawn, sceneSpawn.ObjectId, 0, 0);
                return;
            }

            Spawn(sceneSpawn.ObjectId, 0, sceneSpawn.SceneObjectId, sceneSpawn.Fingerprint, sceneSpawn.OwnerId, sceneSpawn.Mask, sceneSpawn.Values, sceneSpawn.States, 0);
        }

        private void Spawn(uint objectId, uint prefabId, ulong sceneObjectId, uint fingerprint, ulong ownerId, byte mask, List<float> values, List<ReadOnlyMemory<byte>> states, uint sceneId)
        {
            if (objectId == 0 || !SpawnTransform.TryUnpack(mask, values, out Vector3 position, out Quaternion rotation, out Vector3 scale))
            {
                Mismatch(ObjectMismatchKind.InvalidSpawn, objectId, prefabId, sceneObjectId);
                return;
            }

            if ((sceneObjectId != 0 && sceneObjectIds.Contains(sceneObjectId)) || !table.TryAdd(new ObjectRow(objectId, prefabId, sceneObjectId, ownerId, sceneId)))
            {
                Mismatch(ObjectMismatchKind.DuplicateObject, objectId, prefabId, sceneObjectId);
                return;
            }

            if (sceneObjectId != 0)
            {
                sceneObjectIds.Add(sceneObjectId);
            }

            SpawnResult result;
            try
            {
                result = spawner.Spawn(new SpawnedObject(objectId, prefabId, sceneObjectId, fingerprint, ownerId, position, rotation, scale, states, sceneId));
            }
            catch
            {
                Forget(objectId, sceneObjectId);
                throw;
            }

            if (result == SpawnResult.Spawned)
            {
                return;
            }

            Forget(objectId, sceneObjectId);
            Mismatch(KindOf(result), objectId, prefabId, sceneObjectId);
        }

        private void OnDespawn(ulong peerId, ReadOnlyMemory<byte> payload)
        {
            if (!TryDecode(protocol.DespawnCodec, payload, ref despawn))
            {
                return;
            }

            uint objectId = despawn.ObjectId;
            if (!table.TryGet(objectId, out ObjectRow row))
            {
                Mismatch(ObjectMismatchKind.UnknownObject, objectId, 0, 0);
                return;
            }

            try
            {
                spawner.Despawn(objectId);
            }
            finally
            {
                Forget(objectId, row.SceneObjectId);
            }
        }

        private void OnOwnerChange(ulong peerId, ReadOnlyMemory<byte> payload)
        {
            if (!TryDecode(protocol.OwnerChangeCodec, payload, ref ownerChange))
            {
                return;
            }

            uint objectId = ownerChange.ObjectId;
            if (!table.SetOwner(objectId, ownerChange.OwnerId, out ulong previousOwnerId))
            {
                Mismatch(ObjectMismatchKind.UnknownObject, objectId, 0, 0);
                return;
            }

            OnOwnerChanged?.Invoke(new ObjectOwnerChangedArgs(objectId, previousOwnerId, ownerChange.OwnerId));
        }

        private void OnSceneLoad(ulong peerId, ReadOnlyMemory<byte> payload)
        {
            if (!TryDecode(sceneProtocol.LoadCodec, payload, ref sceneLoad))
            {
                return;
            }

            if (sceneLoad.Full)
            {
                foreach (uint sceneId in loadedScenes)
                {
                    if (!sceneLoad.Scenes.Contains(sceneId))
                    {
                        sceneSteps.Add(new SceneStep(sceneId, false));
                    }
                }

                if (sceneBusy && sceneRunning.Load && !sceneLoad.Scenes.Contains(sceneRunning.SceneId))
                {
                    sceneSteps.Add(new SceneStep(sceneRunning.SceneId, false));
                }
            }

            foreach (uint sceneId in sceneLoad.Scenes)
            {
                if (!IsLoadPending(sceneId))
                {
                    sceneSteps.Add(new SceneStep(sceneId, true));
                }
            }

            RunScenes();
        }

        private void OnSceneUnload(ulong peerId, ReadOnlyMemory<byte> payload)
        {
            if (!TryDecode(sceneProtocol.UnloadCodec, payload, ref sceneUnload))
            {
                return;
            }

            foreach (uint sceneId in sceneUnload.Scenes)
            {
                int queued = sceneSteps.FindIndex(step => step.Load && step.SceneId == sceneId);
                if (queued >= 0)
                {
                    sceneSteps.RemoveAt(queued);
                }
                else
                {
                    sceneSteps.Add(new SceneStep(sceneId, false));
                }
            }

            RunScenes();
        }

        private bool IsLoadPending(uint sceneId) =>
            (sceneBusy && sceneRunning.Load && sceneRunning.SceneId == sceneId)
            || sceneSteps.Exists(step => step.Load && step.SceneId == sceneId);

        private void RunScenes()
        {
            while (!sceneBusy && sceneSteps.Count > 0)
            {
                SceneStep step = sceneSteps[0];
                sceneSteps.RemoveAt(0);
                uint sceneId = step.SceneId;
                if (step.Load)
                {
                    if (loadedScenes.Contains(sceneId))
                    {
                        ReportLoaded(sceneId);
                        continue;
                    }

                    sceneBusy = true;
                    sceneRunning = step;
                    if (!sceneHost.TryLoad(sceneId, () => FinishLoad(sceneId), () => FailLoad(sceneId)))
                    {
                        FailLoad(sceneId);
                        return;
                    }
                }
                else if (loadedScenes.Contains(sceneId))
                {
                    sceneBusy = true;
                    sceneRunning = step;
                    sceneHost.Unload(sceneId, () => FinishUnload(sceneId));
                }
            }
        }

        private void FinishLoad(uint sceneId)
        {
            sceneBusy = false;
            loadedScenes.Add(sceneId);
            ReportLoaded(sceneId);
            RunScenes();
        }

        private void FailLoad(uint sceneId)
        {
            sceneBusy = false;
            sceneSteps.Clear();
            OnObjectMismatch?.Invoke(new ObjectMismatchArgs(ObjectMismatchKind.UnknownScene, 0, 0, 0, sceneId));
            session.Abort(StopReason.PrefabMismatch);
        }

        private void FinishUnload(uint sceneId)
        {
            sceneBusy = false;
            loadedScenes.Remove(sceneId);
            RunScenes();
        }

        private void ReportLoaded(uint sceneId)
        {
            if (session.State != ConnectionState.Started)
            {
                return;
            }

            sceneLoaded.Scenes.Clear();
            sceneLoaded.Scenes.Add(sceneId);
            session.EnqueueObjectMessage(sceneProtocol.LoadedCodec.MessageId, sceneProtocol.LoadedCodec.Encode(sceneLoaded).Span);
        }

        private bool TryDecode<T>(IMessageCodec<T> codec, ReadOnlyMemory<byte> payload, ref T value)
        {
            try
            {
                codec.Decode(payload, ref value);
                return true;
            }
            catch (MessageDecodeException)
            {
                Mismatch(ObjectMismatchKind.InvalidMessage, 0, 0, 0);
                return false;
            }
        }

        private static ObjectMismatchKind KindOf(SpawnResult result)
        {
            switch (result)
            {
                case SpawnResult.IncompatiblePrefab:
                    return ObjectMismatchKind.IncompatiblePrefab;
                case SpawnResult.UnknownSceneObject:
                    return ObjectMismatchKind.UnknownSceneObject;
                case SpawnResult.IncompatibleSceneObject:
                    return ObjectMismatchKind.IncompatibleSceneObject;
                case SpawnResult.InvalidSpawn:
                    return ObjectMismatchKind.InvalidSpawn;
                default:
                    return ObjectMismatchKind.UnknownPrefab;
            }
        }

        private void Forget(uint objectId, ulong sceneObjectId)
        {
            table.Remove(objectId);
            sceneObjectIds.Remove(sceneObjectId);
        }

        private void Mismatch(ObjectMismatchKind kind, uint objectId, uint prefabId, ulong sceneObjectId)
        {
            OnObjectMismatch?.Invoke(new ObjectMismatchArgs(kind, objectId, prefabId, sceneObjectId));
            bool isBuildMismatch = kind == ObjectMismatchKind.UnknownPrefab
                || kind == ObjectMismatchKind.IncompatiblePrefab
                || kind == ObjectMismatchKind.UnknownSceneObject
                || kind == ObjectMismatchKind.IncompatibleSceneObject;
            session.Abort(isBuildMismatch ? StopReason.PrefabMismatch : StopReason.ObjectMismatch);
        }

        private void Clear()
        {
            clearing.Clear();
            table.CopyIdsTo(clearing);
            table.Clear();
            sceneObjectIds.Clear();
            sceneSteps.Clear();
            LocalPeerId = 0;
            ServerTickRate = 0;
            for (int index = clearing.Count - 1; index >= 0; index--)
            {
                spawner.Despawn(clearing[index]);
            }
        }

        private readonly struct SceneStep
        {
            public SceneStep(uint sceneId, bool load)
            {
                SceneId = sceneId;
                Load = load;
            }

            public uint SceneId { get; }

            public bool Load { get; }
        }
    }
}
