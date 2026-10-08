using System;
using System.Collections.Generic;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Standalone
{
    internal sealed class StandaloneServerEntityBackend : IServerEntityBackend
    {
        private readonly StandalonePrefabs prefabs;

        private readonly IPhysicsScenes physics;
        private readonly HashSet<uint> bootScenes = new HashSet<uint>();

        public StandaloneServerEntityBackend(StandalonePrefabs prefabs, IPhysicsScenes physics, IReadOnlyList<SceneFile> bootScenes)
        {
            this.physics = physics;
            this.prefabs = prefabs;
            foreach (SceneFile file in bootScenes)
            {
                this.bootScenes.Add(file.SceneId);
            }
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

        public uint SceneIdOf(INetworkEntity entity)
        {
            uint sceneId = ((StandaloneEntity)entity).SceneId;
            return bootScenes.Contains(sceneId) ? 0 : sceneId;
        }

        public void PrepareSpawn(INetworkEntity entity)
        {
            if (physics != null)
            {
                ((StandaloneEntity)entity).AttachBodies(physics);
            }
        }

        public void Activate(INetworkEntity entity)
        {
        }

        public void End(INetworkEntity entity, bool isSceneObject)
        {
            if (physics != null)
            {
                ((StandaloneEntity)entity).DetachBodies(physics);
            }
        }
    }

    internal sealed class StandaloneServerSceneHost : IServerSceneHost
    {
        private readonly FomoxaRegistry registry;
        private readonly ISceneFiles files;
        private readonly StandaloneBehaviours behaviours;
        private readonly Dictionary<uint, List<StandaloneEntity>> scenes = new Dictionary<uint, List<StandaloneEntity>>();
        private readonly List<SceneFile> bootFiles = new List<SceneFile>();
        private readonly Dictionary<uint, List<StandaloneEntity>> bootObjects = new Dictionary<uint, List<StandaloneEntity>>();

        private readonly IPhysicsScenes physics;

        public StandaloneServerSceneHost(FomoxaRegistry registry, ISceneFiles files, StandaloneBehaviours behaviours, IPhysicsScenes physics, IReadOnlyList<SceneFile> bootScenes)
        {
            this.physics = physics;
            this.registry = registry;
            this.files = files;
            this.behaviours = behaviours;
            bootFiles.AddRange(bootScenes);
        }

        public bool Knows(uint sceneId) => files.Knows(sceneId);

        public void PresentSceneObjects(List<INetworkEntity> found)
        {
            bootObjects.Clear();
            foreach (SceneFile file in bootFiles)
            {
                List<StandaloneEntity> sceneObjects = StandaloneSceneObjects.Build(file, behaviours);
                bootObjects.Add(file.SceneId, sceneObjects);
                found.AddRange(sceneObjects);
            }
        }

        public bool TryLoad(uint sceneId, Func<bool> accept, Action loaded, Action<Exception> failed)
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
                failed(exception);
                return true;
            }

            if (!accept())
            {
                return true;
            }

            scenes.Add(sceneId, sceneObjects);
            physics?.LoadScene(file);
            loaded();
            return true;
        }

        public void Unload(uint sceneId, Action unloaded)
        {
            scenes.Remove(sceneId);
            physics?.UnloadScene(sceneId);
            unloaded();
        }

        public void SceneObjectsOf(uint sceneId, List<INetworkEntity> found)
        {
            if (scenes.TryGetValue(sceneId, out List<StandaloneEntity> sceneObjects) || bootObjects.TryGetValue(sceneId, out sceneObjects))
            {
                found.AddRange(sceneObjects);
            }
        }

        public bool Holds(uint sceneId, INetworkEntity entity) =>
            entity is StandaloneEntity standalone
            && ((scenes.TryGetValue(sceneId, out List<StandaloneEntity> sceneObjects) && sceneObjects.Contains(standalone))
                || (bootObjects.TryGetValue(sceneId, out List<StandaloneEntity> bootSceneObjects) && bootSceneObjects.Contains(standalone)));
    }
}
