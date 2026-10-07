using System;
using System.Collections.Generic;
using Fomoxa.Networking;
using Fomoxa.Networking.Objects;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity
{
    internal sealed class UnityServerEntityBackend : IServerEntityBackend
    {
        private readonly PrefabRegistry prefabs;
        private readonly UnityServerSceneHost scenes;

        public UnityServerEntityBackend(PrefabRegistry prefabs, UnityServerSceneHost scenes)
        {
            this.prefabs = prefabs;
            this.scenes = scenes;
        }

        public NetworkPhysics Physics { get; set; }

        public string NameOf(INetworkEntity entity) => ((NetworkObject)entity).name;

        public void ValidateSpawn(INetworkEntity entity)
        {
            var networkObject = (NetworkObject)entity;
            if (!networkObject.gameObject.scene.IsValid())
            {
                throw new ArgumentException($"{networkObject.name} is a prefab asset; spawn an instance of it", nameof(networkObject));
            }
        }

        public uint FingerprintOf(INetworkEntity entity, bool isSceneObject)
        {
            var networkObject = (NetworkObject)entity;
            if (isSceneObject)
            {
                if (!PrefabHash.TryDescribeSceneObject(networkObject, out uint fingerprint, out string error))
                {
                    throw new ArgumentException($"{networkObject.name}: {error}", nameof(networkObject));
                }

                return fingerprint;
            }

            if (prefabs.TryGet(networkObject.PrefabId, out PrefabEntry entry))
            {
                return entry.Fingerprint;
            }

            throw new ArgumentException($"prefab id 0x{networkObject.PrefabId:X8} of {networkObject.name} is not registered", nameof(networkObject));
        }

        public uint SceneIdOf(INetworkEntity entity) => scenes.SceneIdOf(((NetworkObject)entity).gameObject.scene);

        public void PrepareSpawn(INetworkEntity entity)
        {
            ((NetworkObject)entity).CollectBehaviours();
        }

        public void Activate(INetworkEntity entity) => ((NetworkObject)entity).gameObject.SetActive(true);

        public void End(INetworkEntity entity, bool isSceneObject)
        {
            var networkObject = (NetworkObject)entity;
            if (isSceneObject)
            {
                networkObject.gameObject.SetActive(false);
                return;
            }

            networkObject.DestroyGameObject();
        }
    }

    internal sealed class UnityServerSceneHost : IServerSceneHost
    {
        private readonly SceneRegistry registry;
        private readonly Dictionary<uint, LoadedScene> scenes = new Dictionary<uint, LoadedScene>();
        private readonly Dictionary<Scene, uint> idsByScene = new Dictionary<Scene, uint>();
        private readonly HashSet<Scene> unloadingScenes = new HashSet<Scene>();

        public UnityServerSceneHost(SceneRegistry registry)
        {
            this.registry = registry;
        }

        public Func<IReadOnlyList<NetworkObject>> FindSceneObjects { get; set; } = SceneObjects.InLoadedScenes;

        public bool Knows(uint sceneId) => registry.Contains(sceneId);

        public void PresentSceneObjects(List<INetworkEntity> found)
        {
            foreach (NetworkObject sceneObject in FindSceneObjects())
            {
                if (!unloadingScenes.Contains(sceneObject.gameObject.scene))
                {
                    found.Add(sceneObject);
                }
            }
        }

        public bool TryLoad(uint sceneId, Func<bool> accept, Action loaded, Action<Exception> failed)
        {
            if (!registry.TryGet(sceneId, out ISceneLoader loader))
            {
                return false;
            }

            loader.Load(scene => Arrive(sceneId, scene, loader, accept, loaded), failed);
            return true;
        }

        public void Unload(uint sceneId, Action unloaded)
        {
            if (!scenes.Remove(sceneId, out LoadedScene loaded))
            {
                unloaded();
                return;
            }

            Scene scene = loaded.Scene;
            idsByScene.Remove(scene);
            unloadingScenes.Add(scene);
            loaded.Loader.Unload(scene, () =>
            {
                unloadingScenes.Remove(scene);
                unloaded();
            });
        }

        public void SceneObjectsOf(uint sceneId, List<INetworkEntity> found)
        {
            if (!scenes.TryGetValue(sceneId, out LoadedScene loaded))
            {
                return;
            }

            var objects = new List<NetworkObject>();
            SceneObjects.AddFrom(loaded.Scene, objects);
            found.AddRange(objects);
        }

        public bool Holds(uint sceneId, INetworkEntity entity)
        {
            var networkObject = (NetworkObject)entity;
            return networkObject == null || (scenes.TryGetValue(sceneId, out LoadedScene loaded) && networkObject.gameObject.scene == loaded.Scene);
        }

        public bool TryGetScene(uint sceneId, out Scene scene)
        {
            if (scenes.TryGetValue(sceneId, out LoadedScene loaded))
            {
                scene = loaded.Scene;
                return true;
            }

            scene = default;
            return false;
        }

        public uint SceneIdOf(Scene scene) =>
            scene.IsValid() && idsByScene.TryGetValue(scene, out uint sceneId) ? sceneId : 0;

        public bool IsUnloading(Scene scene) => unloadingScenes.Contains(scene);

        private void Arrive(uint sceneId, Scene scene, ISceneLoader loader, Func<bool> accept, Action loaded)
        {
            if (!accept())
            {
                loader.Unload(scene, () => { });
                return;
            }

            scenes.Add(sceneId, new LoadedScene(scene, loader));
            idsByScene[scene] = sceneId;
            loaded();
        }

        private readonly struct LoadedScene
        {
            public LoadedScene(Scene scene, ISceneLoader loader)
            {
                Scene = scene;
                Loader = loader;
            }

            public Scene Scene { get; }

            public ISceneLoader Loader { get; }
        }
    }
}
