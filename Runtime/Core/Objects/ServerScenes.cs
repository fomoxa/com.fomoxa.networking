using System;
using System.Collections.Generic;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;

namespace Fomoxa.Networking.Objects
{
    public sealed class ServerScenes
    {
        private readonly ServerObjects objects;
        private readonly ServerSession session;
        private readonly SceneProtocol protocol;
        private readonly List<uint> global = new List<uint>();
        private readonly Dictionary<uint, HashSet<ulong>> perPeer = new Dictionary<uint, HashSet<ulong>>();
        private readonly Dictionary<ulong, HashSet<uint>> loadedByPeer = new Dictionary<ulong, HashSet<uint>>();
        private readonly Dictionary<ulong, List<uint>> outgoing = new Dictionary<ulong, List<uint>>();
        private readonly List<ulong> startedPeers = new List<ulong>();
        private readonly List<ulong> changedPeers = new List<ulong>();
        private readonly SceneLoad load = new SceneLoad();
        private readonly SceneUnload unload = new SceneUnload();
        private SceneLoaded loaded = new SceneLoaded();

        internal ServerScenes(ServerObjects objects, ServerSession session, SceneProtocol protocol)
        {
            this.objects = objects;
            this.session = session;
            this.protocol = protocol;
            session.Dispatcher.Register(protocol.LoadedCodec.MessageId, OnLoaded);
            session.OnServerConnectionState += OnServerState;
            session.OnRemoteConnectionState += OnPeerState;
        }

        public event Action<uint> OnSceneAdded;

        public event Action<uint> OnSceneRemoved;

        public IReadOnlyList<uint> Global => global;

        public bool Contains(uint sceneId) => global.Contains(sceneId) || perPeer.ContainsKey(sceneId);

        public bool IsGlobal(uint sceneId) => global.Contains(sceneId);

        public bool HasLoaded(ulong peerId, uint sceneId) =>
            loadedByPeer.TryGetValue(peerId, out HashSet<uint> scenes) && scenes.Contains(sceneId);

        public bool IsAssigned(ulong peerId, uint sceneId) =>
            global.Contains(sceneId) || (perPeer.TryGetValue(sceneId, out HashSet<ulong> peers) && peers.Contains(peerId));

        public IReadOnlyCollection<ulong> PeersOf(uint sceneId)
        {
            if (perPeer.TryGetValue(sceneId, out HashSet<ulong> peers))
            {
                return new List<ulong>(peers);
            }

            var started = new List<ulong>();
            if (global.Contains(sceneId))
            {
                session.CopyStartedPeerIds(started);
            }

            return started;
        }

        public void LoadGlobal(IReadOnlyList<uint> sceneIds, bool replace = false)
        {
            EnsureStarted();
            EnsureIds(sceneIds);
            if (replace && global.Count > 0)
            {
                UnloadGlobal(new List<uint>(global));
            }

            CollectStartedPeers();
            foreach (uint sceneId in sceneIds)
            {
                if (global.Contains(sceneId))
                {
                    continue;
                }

                bool promoted = perPeer.Remove(sceneId, out HashSet<ulong> holders);
                global.Add(sceneId);
                if (!promoted)
                {
                    OnSceneAdded?.Invoke(sceneId);
                }

                foreach (ulong peerId in startedPeers)
                {
                    if (!promoted || !holders.Contains(peerId))
                    {
                        Queue(peerId, sceneId);
                    }
                }
            }

            SendLoads();
        }

        public void UnloadGlobal(IReadOnlyList<uint> sceneIds)
        {
            EnsureStarted();
            EnsureIds(sceneIds);
            foreach (uint sceneId in sceneIds)
            {
                if (!global.Contains(sceneId))
                {
                    throw new ArgumentException($"scene 0x{sceneId:X8} is not a global scene", nameof(sceneIds));
                }
            }

            CollectStartedPeers();
            foreach (uint sceneId in sceneIds)
            {
                if (!global.Remove(sceneId))
                {
                    continue;
                }

                objects.DespawnScene(sceneId);
                foreach (ulong peerId in startedPeers)
                {
                    Forget(peerId, sceneId);
                    Queue(peerId, sceneId);
                }

                OnSceneRemoved?.Invoke(sceneId);
            }

            SendUnloads();
        }

        public int LoadForPeers(uint sceneId, IReadOnlyList<ulong> peerIds)
        {
            EnsureStarted();
            EnsureId(sceneId);
            if (peerIds == null)
            {
                throw new ArgumentNullException(nameof(peerIds));
            }

            if (global.Contains(sceneId))
            {
                throw new ArgumentException($"scene 0x{sceneId:X8} is a global scene", nameof(sceneId));
            }

            if (!perPeer.TryGetValue(sceneId, out HashSet<ulong> holders))
            {
                holders = new HashSet<ulong>();
                perPeer.Add(sceneId, holders);
                OnSceneAdded?.Invoke(sceneId);
            }

            int changed = 0;
            foreach (ulong peerId in peerIds)
            {
                if (session.PeerState(peerId) == ConnectionState.Started && holders.Add(peerId))
                {
                    Queue(peerId, sceneId);
                    changed++;
                }
            }

            SendLoads();
            return changed;
        }

        public int UnloadForPeers(uint sceneId, IReadOnlyList<ulong> peerIds)
        {
            EnsureStarted();
            HashSet<ulong> holders = PerPeerScene(sceneId);
            if (peerIds == null)
            {
                throw new ArgumentNullException(nameof(peerIds));
            }

            changedPeers.Clear();
            foreach (ulong peerId in peerIds)
            {
                if (holders.Remove(peerId))
                {
                    Forget(peerId, sceneId);
                    changedPeers.Add(peerId);
                }
            }

            foreach (ulong peerId in changedPeers)
            {
                objects.RebuildObserversOfPeer(peerId);
                Queue(peerId, sceneId);
            }

            SendUnloads();
            if (changedPeers.Count > 0 && holders.Count == 0)
            {
                perPeer.Remove(sceneId);
                objects.DespawnScene(sceneId);
                OnSceneRemoved?.Invoke(sceneId);
            }

            return changedPeers.Count;
        }

        public void Unload(uint sceneId)
        {
            EnsureStarted();
            HashSet<ulong> holders = PerPeerScene(sceneId);
            objects.DespawnScene(sceneId);
            perPeer.Remove(sceneId);
            foreach (ulong peerId in holders)
            {
                Forget(peerId, sceneId);
                Queue(peerId, sceneId);
            }

            SendUnloads();
            OnSceneRemoved?.Invoke(sceneId);
        }

        internal void StartPeer(ulong peerId)
        {
            loadedByPeer[peerId] = new HashSet<uint>();
            load.Full = true;
            load.Scenes.Clear();
            load.Scenes.AddRange(global);
            session.EnqueueObjectMessage(peerId, protocol.LoadCodec.MessageId, protocol.LoadCodec.Encode(load).Span);
        }

        private void OnLoaded(ulong peerId, ReadOnlyMemory<byte> payload)
        {
            protocol.LoadedCodec.Decode(payload, ref loaded);
            if (!loadedByPeer.TryGetValue(peerId, out HashSet<uint> scenes))
            {
                return;
            }

            bool added = false;
            foreach (uint sceneId in loaded.Scenes)
            {
                if (IsAssigned(peerId, sceneId) && scenes.Add(sceneId))
                {
                    added = true;
                }
            }

            if (added)
            {
                objects.RebuildObserversOfPeer(peerId);
            }
        }

        private HashSet<ulong> PerPeerScene(uint sceneId)
        {
            EnsureId(sceneId);
            if (global.Contains(sceneId))
            {
                throw new ArgumentException($"scene 0x{sceneId:X8} is a global scene", nameof(sceneId));
            }

            if (!perPeer.TryGetValue(sceneId, out HashSet<ulong> holders))
            {
                throw new ArgumentException($"scene 0x{sceneId:X8} is not loaded", nameof(sceneId));
            }

            return holders;
        }

        private void Forget(ulong peerId, uint sceneId)
        {
            if (loadedByPeer.TryGetValue(peerId, out HashSet<uint> scenes))
            {
                scenes.Remove(sceneId);
            }
        }

        private void Queue(ulong peerId, uint sceneId)
        {
            if (!outgoing.TryGetValue(peerId, out List<uint> scenes))
            {
                scenes = new List<uint>();
                outgoing.Add(peerId, scenes);
            }

            scenes.Add(sceneId);
        }

        private void SendLoads()
        {
            load.Full = false;
            foreach (KeyValuePair<ulong, List<uint>> pending in outgoing)
            {
                if (pending.Value.Count == 0)
                {
                    continue;
                }

                load.Scenes.Clear();
                load.Scenes.AddRange(pending.Value);
                session.EnqueueObjectMessage(pending.Key, protocol.LoadCodec.MessageId, protocol.LoadCodec.Encode(load).Span);
                pending.Value.Clear();
            }
        }

        private void SendUnloads()
        {
            foreach (KeyValuePair<ulong, List<uint>> pending in outgoing)
            {
                if (pending.Value.Count == 0)
                {
                    continue;
                }

                unload.Scenes.Clear();
                unload.Scenes.AddRange(pending.Value);
                session.EnqueueObjectMessage(pending.Key, protocol.UnloadCodec.MessageId, protocol.UnloadCodec.Encode(unload).Span);
                pending.Value.Clear();
            }
        }

        private void CollectStartedPeers()
        {
            startedPeers.Clear();
            session.CopyStartedPeerIds(startedPeers);
        }

        private void EnsureStarted()
        {
            objects.EnsureNotDeciding();
            if (session.State != ServerState.Started)
            {
                throw new InvalidOperationException("the server session is not started");
            }
        }

        private static void EnsureIds(IReadOnlyList<uint> sceneIds)
        {
            if (sceneIds == null)
            {
                throw new ArgumentNullException(nameof(sceneIds));
            }

            foreach (uint sceneId in sceneIds)
            {
                EnsureId(sceneId);
            }
        }

        private static void EnsureId(uint sceneId)
        {
            if (sceneId == 0)
            {
                throw new ArgumentException("scene id 0 means no network scene", nameof(sceneId));
            }
        }

        private void OnPeerState(ConnectionStateArgs args)
        {
            if (args.State != ConnectionState.Stopped)
            {
                return;
            }

            loadedByPeer.Remove(args.PeerId);
            outgoing.Remove(args.PeerId);
            foreach (HashSet<ulong> holders in perPeer.Values)
            {
                holders.Remove(args.PeerId);
            }
        }

        private void OnServerState(ServerConnectionStateArgs args)
        {
            if (args.State == ServerState.Stopped)
            {
                global.Clear();
                perPeer.Clear();
                loadedByPeer.Clear();
                outgoing.Clear();
            }
        }
    }
}
