using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity
{
    public sealed class SceneRegistry
    {
        private readonly Dictionary<uint, ISceneLoader> loaders = new Dictionary<uint, ISceneLoader>();
        private readonly Dictionary<string, uint> idsByPath = new Dictionary<string, uint>(StringComparer.Ordinal);
        private readonly Dictionary<uint, TextAsset> files = new Dictionary<uint, TextAsset>();

        public int Count => loaders.Count;

        public void Register(uint sceneId, ISceneLoader loader)
        {
            if (sceneId == 0)
            {
                throw new ArgumentException("scene id 0 means no network scene", nameof(sceneId));
            }

            if (loader == null)
            {
                throw new ArgumentNullException(nameof(loader));
            }

            if (!loaders.TryAdd(sceneId, loader))
            {
                throw new ArgumentException($"scene id 0x{sceneId:X8} is already registered", nameof(sceneId));
            }
        }

        public bool Contains(uint sceneId) => loaders.ContainsKey(sceneId);

        public bool TryGetSceneFile(uint sceneId, out byte[] bytes)
        {
            if (files.TryGetValue(sceneId, out TextAsset file) && file != null)
            {
                bytes = file.bytes;
                return true;
            }

            bytes = null;
            return false;
        }

        public uint IdOf(string path) =>
            path != null && idsByPath.TryGetValue(path, out uint sceneId) ? sceneId : 0;

        internal bool TryGet(uint sceneId, out ISceneLoader loader) => loaders.TryGetValue(sceneId, out loader);

        internal void RegisterBuildScenes(NetworkSceneList list)
        {
            for (int index = 0; index < list.Count; index++)
            {
                uint sceneId = list.SceneIdAt(index);
                string path = list.PathAt(index);
                Register(sceneId, new BuildSceneLoader(path));
                idsByPath[path] = sceneId;
                TextAsset file = list.SceneFileAt(index);
                if (file != null)
                {
                    files[sceneId] = file;
                }
            }
        }

        private sealed class BuildSceneLoader : ISceneLoader
        {
            private readonly string path;

            public BuildSceneLoader(string path)
            {
                this.path = path;
            }

            public void Load(Action<Scene> loaded, Action<Exception> failed)
            {
                AsyncOperation operation;
                try
                {
                    operation = SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
                }
                catch (Exception exception)
                {
                    failed(exception);
                    return;
                }

                if (operation == null)
                {
                    failed(new InvalidOperationException($"scene {path} could not be loaded; it is not in the build"));
                    return;
                }

                operation.completed += _ =>
                {
                    Scene scene = SceneManager.GetSceneByPath(path);
                    if (scene.IsValid() && scene.isLoaded)
                    {
                        loaded(scene);
                    }
                    else
                    {
                        failed(new InvalidOperationException($"scene {path} was not found after loading"));
                    }
                };
            }

            public void Unload(Scene scene, Action unloaded)
            {
                AsyncOperation operation = scene.IsValid() && scene.isLoaded ? SceneManager.UnloadSceneAsync(scene) : null;
                if (operation == null)
                {
                    unloaded();
                    return;
                }

                operation.completed += _ => unloaded();
            }
        }
    }
}
