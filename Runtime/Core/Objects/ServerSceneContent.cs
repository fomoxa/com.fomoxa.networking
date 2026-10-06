using System;
using System.Collections.Generic;
using Fomoxa.Networking.Sessions;

namespace Fomoxa.Networking.Objects
{
    internal sealed class ServerSceneContent
    {
        private readonly ServerSession session;
        private readonly ServerEntities entities;
        private readonly IServerSceneHost host;
        private readonly NetworkLog log;
        private readonly HashSet<uint> loaded = new HashSet<uint>();
        private readonly HashSet<uint> loading = new HashSet<uint>();
        private readonly HashSet<uint> removedWhileLoading = new HashSet<uint>();
        private readonly HashSet<uint> unloading = new HashSet<uint>();
        private readonly HashSet<uint> loadAfterUnload = new HashSet<uint>();
        private int epoch;

        public ServerSceneContent(ServerSession session, ServerScenes scenes, ServerEntities entities, IServerSceneHost host, NetworkLog log)
        {
            this.session = session;
            this.entities = entities;
            this.host = host;
            this.log = log;
            scenes.OnSceneAdded += Load;
            scenes.OnSceneRemoved += Remove;
            session.OnServerConnectionState += ReleaseAllWhenStopped;
        }

        public event Action<uint> OnLoaded;

        public event Action<uint, Exception> OnLoadFailed;

        public bool IsLoading(uint sceneId) => loading.Contains(sceneId);

        public bool IsLoaded(uint sceneId) => loaded.Contains(sceneId);

        private void Load(uint sceneId)
        {
            if (loading.Contains(sceneId))
            {
                removedWhileLoading.Remove(sceneId);
                return;
            }

            if (unloading.Contains(sceneId))
            {
                loadAfterUnload.Add(sceneId);
                return;
            }

            if (loaded.Contains(sceneId))
            {
                return;
            }

            int attempt = epoch;
            loading.Add(sceneId);
            if (!host.TryLoad(sceneId, () => Accept(sceneId, attempt), () => Finish(sceneId), exception => Fail(sceneId, exception, attempt)))
            {
                loading.Remove(sceneId);
            }
        }

        private bool Accept(uint sceneId, int attempt)
        {
            if (attempt != epoch)
            {
                return false;
            }

            loading.Remove(sceneId);
            if (removedWhileLoading.Remove(sceneId) || session.State != ServerState.Started)
            {
                return false;
            }

            loaded.Add(sceneId);
            return true;
        }

        private void Finish(uint sceneId)
        {
            var found = new List<INetworkEntity>();
            host.SceneObjectsOf(sceneId, found);
            entities.SpawnSceneObjects(found);
            OnLoaded?.Invoke(sceneId);
        }

        private void Fail(uint sceneId, Exception exception, int attempt)
        {
            if (attempt != epoch)
            {
                return;
            }

            loading.Remove(sceneId);
            removedWhileLoading.Remove(sceneId);
            log.Exception(exception);
            OnLoadFailed?.Invoke(sceneId, exception);
        }

        private void Remove(uint sceneId)
        {
            if (loadAfterUnload.Remove(sceneId))
            {
                return;
            }

            if (loading.Contains(sceneId))
            {
                removedWhileLoading.Add(sceneId);
                return;
            }

            if (loaded.Remove(sceneId))
            {
                Release(sceneId);
            }
        }

        private void Release(uint sceneId)
        {
            entities.ForgetSceneObjects(entity => host.Holds(sceneId, entity));
            unloading.Add(sceneId);
            host.Unload(sceneId, () => FinishUnload(sceneId));
        }

        private void FinishUnload(uint sceneId)
        {
            unloading.Remove(sceneId);
            if (loadAfterUnload.Remove(sceneId))
            {
                Load(sceneId);
            }
        }

        private void ReleaseAllWhenStopped(ServerConnectionStateArgs args)
        {
            if (args.State != ServerState.Stopped)
            {
                return;
            }

            epoch++;
            loading.Clear();
            removedWhileLoading.Clear();
            loadAfterUnload.Clear();
            var releasing = new List<uint>(loaded);
            loaded.Clear();
            foreach (uint sceneId in releasing)
            {
                Release(sceneId);
            }
        }
    }
}
