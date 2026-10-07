using System;
using System.Collections.Generic;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Prediction;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Standalone
{
    internal sealed class StandaloneClientEntityBackend : IClientEntityBackend
    {
        private readonly StandalonePrefabs prefabs;
        private readonly Dictionary<ulong, StandaloneEntity> sceneObjects = new Dictionary<ulong, StandaloneEntity>();
        private readonly Dictionary<uint, List<StandaloneEntity>> scenes = new Dictionary<uint, List<StandaloneEntity>>();

        private readonly IPhysicsScenes physics;

        public StandaloneClientEntityBackend(StandalonePrefabs prefabs, IPhysicsScenes physics)
        {
            this.prefabs = prefabs;
            this.physics = physics;
        }

        public SpawnResult CheckPrefab(in SpawnedObject spawned)
        {
            if (!prefabs.TryGet(spawned.PrefabId, out uint fingerprint, out Func<StandaloneEntity> _))
            {
                return SpawnResult.UnknownPrefab;
            }

            return fingerprint == spawned.PrefabFingerprint ? SpawnResult.Spawned : SpawnResult.IncompatiblePrefab;
        }

        public INetworkEntity Create(in SpawnedObject spawned)
        {
            prefabs.TryGet(spawned.PrefabId, out uint _, out Func<StandaloneEntity> create);
            StandaloneEntity instance = create()
                ?? throw new InvalidOperationException($"the factory of prefab 0x{spawned.PrefabId:X8} returned no StandaloneEntity");
            if (instance.Record != null)
            {
                throw new InvalidOperationException($"the factory of prefab 0x{spawned.PrefabId:X8} returned an entity that is already spawned");
            }

            instance.SceneId = spawned.SceneId;
            Place(instance, spawned);
            Attach(instance);
            return instance;
        }

        public SpawnResult PlaceSceneObject(in SpawnedObject spawned, out INetworkEntity entity)
        {
            if (!sceneObjects.TryGetValue(spawned.SceneObjectId, out StandaloneEntity instance))
            {
                entity = null;
                return SpawnResult.UnknownSceneObject;
            }

            entity = instance;
            if (instance.SceneFingerprint != spawned.PrefabFingerprint)
            {
                return SpawnResult.IncompatibleSceneObject;
            }

            Place(instance, spawned);
            Attach(instance);
            return SpawnResult.Spawned;
        }

        public void End(INetworkEntity entity)
        {
            if (physics != null)
            {
                ((StandaloneEntity)entity).DetachBodies(physics);
            }
        }

        public void HideOnHost(INetworkEntity entity)
        {
        }

        public void ShowOnHost(INetworkEntity entity)
        {
        }

        public void PrepareSceneObjects()
        {
        }

        public void AddScene(uint sceneId, List<StandaloneEntity> loaded)
        {
            var added = new List<StandaloneEntity>(loaded.Count);
            foreach (StandaloneEntity sceneObject in loaded)
            {
                if (sceneObjects.TryAdd(sceneObject.SceneObjectId, sceneObject))
                {
                    added.Add(sceneObject);
                }
            }

            scenes[sceneId] = added;
        }

        public void RemoveScene(uint sceneId)
        {
            if (!scenes.Remove(sceneId, out List<StandaloneEntity> removed))
            {
                return;
            }

            foreach (StandaloneEntity sceneObject in removed)
            {
                sceneObjects.Remove(sceneObject.SceneObjectId);
            }
        }

        private void Attach(StandaloneEntity instance)
        {
            if (physics != null)
            {
                instance.AttachBodies(physics);
            }
        }

        private static void Place(StandaloneEntity instance, in SpawnedObject spawned)
        {
            instance.Position = spawned.Position;
            instance.Rotation = spawned.Rotation;
            instance.Scale = spawned.Scale;
        }
    }

    internal sealed class StandaloneClientSceneHost : ISceneHost
    {
        private readonly FomoxaRegistry registry;
        private readonly ISceneFiles files;
        private readonly StandaloneBehaviours behaviours;
        private readonly StandaloneClientEntityBackend entities;
        private readonly NetworkLog log;

        private readonly IPhysicsScenes physics;

        public StandaloneClientSceneHost(FomoxaRegistry registry, ISceneFiles files, StandaloneBehaviours behaviours, StandaloneClientEntityBackend entities, NetworkLog log, IPhysicsScenes physics)
        {
            this.physics = physics;
            this.registry = registry;
            this.files = files;
            this.behaviours = behaviours;
            this.entities = entities;
            this.log = log;
        }

        public bool TryLoad(uint sceneId, Action loaded, Action failed)
        {
            if (!files.Knows(sceneId))
            {
                return false;
            }

            SceneFile file;
            List<StandaloneEntity> sceneObjects;
            try
            {
                file = StandaloneSceneObjects.Read(registry, files, sceneId);
                sceneObjects = StandaloneSceneObjects.Build(file, behaviours);
            }
            catch (Exception exception)
            {
                log.Exception(exception);
                failed();
                return true;
            }

            entities.AddScene(sceneId, sceneObjects);
            physics?.LoadScene(file);
            loaded();
            return true;
        }

        public void Unload(uint sceneId, Action unloaded)
        {
            entities.RemoveScene(sceneId);
            physics?.UnloadScene(sceneId);
            unloaded();
        }
    }

    internal sealed class StandalonePredictionBackend : IClientPredictionBackend
    {
        private readonly IPhysicsScenes physics;

        public StandalonePredictionBackend(IPhysicsScenes physics)
        {
            this.physics = physics;
        }

        public void WorldsOf(INetworkEntity entity, List<IPhysicsSimulation> worlds) => physics?.WorldsOf(entity, worlds);

        public PhysicsHistory HistoryOf(IPhysicsSimulation world, int capacity) =>
            physics != null ? physics.HistoryOf(world, capacity) : throw new NotSupportedException("the standalone backend has no physics worlds");

        public void PlaceProxy(INetworkEntity entity) => physics?.PlaceProxy(entity);

        public void EndProxy(INetworkEntity entity) => physics?.EndProxy(entity);

        public void ForgetDestroyedProxies()
        {
        }

        public void BeginCorrection(INetworkEntity entity)
        {
        }

        public void EndCorrection(INetworkEntity entity)
        {
        }
    }
}
