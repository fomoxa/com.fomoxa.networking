using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fomoxa.Networking.Objects
{
    public enum SpawnResult
    {
        Spawned,
        UnknownPrefab,
        IncompatiblePrefab,
        UnknownSceneObject,
        IncompatibleSceneObject,
        InvalidSpawn,
    }

    public interface ISceneHost
    {
        bool TryLoad(uint sceneId, Action loaded, Action failed);

        void Unload(uint sceneId, Action unloaded);
    }

    public enum ObjectMismatchKind
    {
        UnknownPrefab,
        IncompatiblePrefab,
        UnknownSceneObject,
        IncompatibleSceneObject,
        DuplicateObject,
        UnknownObject,
        InvalidSpawn,
        InvalidMessage,
        UnknownScene,
    }

    public readonly struct SpawnedObject
    {
        public SpawnedObject(uint objectId, uint prefabId, ulong sceneObjectId, uint prefabFingerprint, ulong ownerId, Vector3 position, Quaternion rotation, Vector3 scale, IReadOnlyList<ReadOnlyMemory<byte>> states)
            : this(objectId, prefabId, sceneObjectId, prefabFingerprint, ownerId, position, rotation, scale, states, 0)
        {
        }

        public SpawnedObject(uint objectId, uint prefabId, ulong sceneObjectId, uint prefabFingerprint, ulong ownerId, Vector3 position, Quaternion rotation, Vector3 scale, IReadOnlyList<ReadOnlyMemory<byte>> states, uint sceneId)
        {
            SceneId = sceneId;
            ObjectId = objectId;
            PrefabId = prefabId;
            SceneObjectId = sceneObjectId;
            PrefabFingerprint = prefabFingerprint;
            OwnerId = ownerId;
            Position = position;
            Rotation = rotation;
            Scale = scale;
            States = states;
        }

        public uint ObjectId { get; }

        public uint PrefabId { get; }

        public ulong SceneObjectId { get; }

        public uint PrefabFingerprint { get; }

        public ulong OwnerId { get; }

        public Vector3 Position { get; }

        public Quaternion Rotation { get; }

        public Vector3 Scale { get; }

        public IReadOnlyList<ReadOnlyMemory<byte>> States { get; }

        public uint SceneId { get; }
    }

    public interface IObjectSpawner
    {
        SpawnResult Spawn(in SpawnedObject spawned);

        void Despawn(uint objectId);
    }

    public readonly struct ObjectOwnerChangedArgs
    {
        public ObjectOwnerChangedArgs(uint objectId, ulong previousOwnerId, ulong ownerId)
        {
            ObjectId = objectId;
            PreviousOwnerId = previousOwnerId;
            OwnerId = ownerId;
        }

        public uint ObjectId { get; }

        public ulong PreviousOwnerId { get; }

        public ulong OwnerId { get; }
    }

    public readonly struct ObserverAddedArgs
    {
        public ObserverAddedArgs(uint objectId, ulong peerId)
        {
            ObjectId = objectId;
            PeerId = peerId;
        }

        public uint ObjectId { get; }

        public ulong PeerId { get; }
    }

    public readonly struct ObjectMismatchArgs
    {
        public ObjectMismatchArgs(ObjectMismatchKind kind, uint objectId, uint prefabId, ulong sceneObjectId)
            : this(kind, objectId, prefabId, sceneObjectId, 0)
        {
        }

        public ObjectMismatchArgs(ObjectMismatchKind kind, uint objectId, uint prefabId, ulong sceneObjectId, uint sceneId)
        {
            Kind = kind;
            ObjectId = objectId;
            PrefabId = prefabId;
            SceneObjectId = sceneObjectId;
            SceneId = sceneId;
        }

        public ObjectMismatchKind Kind { get; }

        public uint ObjectId { get; }

        public uint PrefabId { get; }

        public ulong SceneObjectId { get; }

        public uint SceneId { get; }
    }
}
