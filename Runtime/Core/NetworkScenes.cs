using System;
using System.Collections.Generic;
using Fomoxa.Networking.Objects;

namespace Fomoxa.Networking
{
    public sealed class NetworkScenes
    {
        private readonly ServerManager server;

        internal NetworkScenes(ServerManager server)
        {
            this.server = server;
        }

        public event Action<uint> OnLoaded;

        public event Action<uint, Exception> OnLoadFailed;

        public IReadOnlyList<uint> Global => Tier.Global;

        internal ServerManager Server => server;

        private ServerScenes Tier => server.Objects.Scenes;

        public bool Contains(uint sceneId) => Tier.Contains(sceneId);

        public bool IsGlobal(uint sceneId) => Tier.IsGlobal(sceneId);

        public bool HasLoaded(ulong peerId, uint sceneId) => Tier.HasLoaded(peerId, sceneId);

        public IReadOnlyCollection<ulong> PeersOf(uint sceneId) => Tier.PeersOf(sceneId);

        public void LoadGlobal(IReadOnlyList<uint> sceneIds, bool replace = false)
        {
            EnsureRegistered(sceneIds);
            Tier.LoadGlobal(sceneIds, replace);
        }

        public void UnloadGlobal(IReadOnlyList<uint> sceneIds) => Tier.UnloadGlobal(sceneIds);

        public int LoadForPeers(uint sceneId, IReadOnlyList<ulong> peerIds)
        {
            EnsureRegistered(sceneId);
            return Tier.LoadForPeers(sceneId, peerIds);
        }

        public int UnloadForPeers(uint sceneId, IReadOnlyList<ulong> peerIds) => Tier.UnloadForPeers(sceneId, peerIds);

        public void Unload(uint sceneId) => Tier.Unload(sceneId);

        internal void RaiseLoaded(uint sceneId) => OnLoaded?.Invoke(sceneId);

        internal void RaiseLoadFailed(uint sceneId, Exception exception) => OnLoadFailed?.Invoke(sceneId, exception);

        private void EnsureRegistered(IReadOnlyList<uint> sceneIds)
        {
            if (sceneIds == null)
            {
                throw new ArgumentNullException(nameof(sceneIds));
            }

            foreach (uint sceneId in sceneIds)
            {
                EnsureRegistered(sceneId);
            }
        }

        private void EnsureRegistered(uint sceneId)
        {
            if (sceneId != 0 && !server.SceneHost.Knows(sceneId))
            {
                throw new ArgumentException($"scene 0x{sceneId:X8} is not in the scene registry", nameof(sceneId));
            }
        }
    }
}
