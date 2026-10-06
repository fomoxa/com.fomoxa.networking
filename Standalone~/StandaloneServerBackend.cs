using System;
using System.Collections.Generic;
using Fomoxa.Networking.Objects;

namespace Fomoxa.Networking.Standalone
{
    internal sealed class StandaloneServerEntityBackend : IServerEntityBackend
    {
        private readonly StandalonePrefabs prefabs;

        public StandaloneServerEntityBackend(StandalonePrefabs prefabs)
        {
            this.prefabs = prefabs;
        }

        public string NameOf(INetworkEntity entity) =>
            entity.SceneObjectId != 0 ? $"scene object 0x{entity.SceneObjectId:X16}" : $"prefab 0x{entity.PrefabId:X8}";

        public void ValidateSpawn(INetworkEntity entity)
        {
            if (!(entity is StandaloneEntity))
            {
                throw new ArgumentException($"{entity.GetType().Name} is not a StandaloneEntity; the standalone backend spawns StandaloneEntity and its subclasses", nameof(entity));
            }
        }

        public uint FingerprintOf(INetworkEntity entity, bool isSceneObject)
        {
            if (isSceneObject)
            {
                return ((StandaloneEntity)entity).SceneFingerprint;
            }

            if (prefabs.TryGet(entity.PrefabId, out uint fingerprint, out Func<StandaloneEntity> _))
            {
                return fingerprint;
            }

            throw new ArgumentException($"prefab id 0x{entity.PrefabId:X8} is not registered with StandalonePrefabs", nameof(entity));
        }

        public uint SceneIdOf(INetworkEntity entity) => ((StandaloneEntity)entity).SceneId;

        public void PrepareSpawn(INetworkEntity entity)
        {
        }

        public void Activate(INetworkEntity entity)
        {
        }

        public void End(INetworkEntity entity, bool isSceneObject)
        {
        }
    }

    internal sealed class StandaloneServerSceneHost : IServerSceneHost
    {
        private readonly FomoxaRegistry registry;
        private readonly ISceneFiles files;
        private readonly StandaloneBehaviours behaviours;
        private readonly Dictionary<uint, List<StandaloneEntity>> scenes = new Dictionary<uint, List<StandaloneEntity>>();

        public StandaloneServerSceneHost(FomoxaRegistry registry, ISceneFiles files, StandaloneBehaviours behaviours)
        {
            this.registry = registry;
            this.files = files;
            this.behaviours = behaviours;
        }

        public bool Knows(uint sceneId) => files.Knows(sceneId);

        public void PresentSceneObjects(List<INetworkEntity> found)
        {
        }

        public bool TryLoad(uint sceneId, Func<bool> accept, Action loaded, Action<Exception> failed)
        {
            if (!files.Knows(sceneId))
            {
                return false;
            }

            List<StandaloneEntity> sceneObjects;
            try
            {
                sceneObjects = StandaloneSceneObjects.Build(registry, files, behaviours, sceneId);
            }
            catch (Exception exception)
            {
                failed(exception);
                return true;
            }

            if (!accept())
            {
                return true;
            }

            scenes.Add(sceneId, sceneObjects);
            loaded();
            return true;
        }

        public void Unload(uint sceneId, Action unloaded)
        {
            scenes.Remove(sceneId);
            unloaded();
        }

        public void SceneObjectsOf(uint sceneId, List<INetworkEntity> found)
        {
            if (scenes.TryGetValue(sceneId, out List<StandaloneEntity> sceneObjects))
            {
                found.AddRange(sceneObjects);
            }
        }

        public bool Holds(uint sceneId, INetworkEntity entity) =>
            scenes.TryGetValue(sceneId, out List<StandaloneEntity> sceneObjects) && entity is StandaloneEntity standalone && sceneObjects.Contains(standalone);
    }
}
