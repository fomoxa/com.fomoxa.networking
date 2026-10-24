using System;
using System.Collections.Generic;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Sessions;

namespace Fomoxa.Networking
{
    public sealed class ObserverGroups
    {
        private readonly ServerEntities entities;
        private readonly Dictionary<uint, Members> groups = new Dictionary<uint, Members>();
        private readonly Dictionary<ulong, List<uint>> groupsOfPeer = new Dictionary<ulong, List<uint>>();
        private readonly Dictionary<INetworkEntity, List<uint>> groupsOfEntity = new Dictionary<INetworkEntity, List<uint>>();
        private readonly List<uint> affected = new List<uint>();

        internal ObserverGroups(ServerEntities entities)
        {
            this.entities = entities;
        }

        public bool Contains(uint groupId, ulong peerId) =>
            groups.TryGetValue(groupId, out Members members) && members.Peers.Contains(peerId);

        public bool Contains(uint groupId, INetworkEntity entity) =>
            entity != null && groups.TryGetValue(groupId, out Members members) && members.Entities.Contains(entity);

        public void Add(uint groupId, ulong peerId)
        {
            entities.Objects.EnsureNotDeciding();
            Members members = MembersOf(groupId);
            if (!members.Peers.Add(peerId))
            {
                return;
            }

            ListOf(groupsOfPeer, peerId).Add(groupId);
            RebuildEntitiesOf(members, peerId);
        }

        public bool Remove(uint groupId, ulong peerId)
        {
            entities.Objects.EnsureNotDeciding();
            if (!groups.TryGetValue(groupId, out Members members) || !members.Peers.Remove(peerId))
            {
                return false;
            }

            RemoveFrom(groupsOfPeer, peerId, groupId);
            RebuildEntitiesOf(members, peerId);
            DropIfEmpty(groupId, members);
            return true;
        }

        public void Add(uint groupId, INetworkEntity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            entities.Objects.EnsureNotDeciding();
            if (!MembersOf(groupId).Entities.Add(entity))
            {
                return;
            }

            ListOf(groupsOfEntity, entity).Add(groupId);
            RebuildEntity(entity);
        }

        public bool Remove(uint groupId, INetworkEntity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            entities.Objects.EnsureNotDeciding();
            if (!groups.TryGetValue(groupId, out Members members) || !members.Entities.Remove(entity))
            {
                return false;
            }

            RemoveFrom(groupsOfEntity, entity, groupId);
            RebuildEntity(entity);
            DropIfEmpty(groupId, members);
            return true;
        }

        internal List<uint> GroupIdsOf(INetworkEntity entity) =>
            groupsOfEntity.TryGetValue(entity, out List<uint> groupIds) ? groupIds : null;

        internal bool Shares(List<uint> groupIds, ulong peerId)
        {
            foreach (uint groupId in groupIds)
            {
                if (groups[groupId].Peers.Contains(peerId))
                {
                    return true;
                }
            }

            return false;
        }

        internal void CollectPeers(List<uint> groupIds, HashSet<ulong> peers)
        {
            foreach (uint groupId in groupIds)
            {
                peers.UnionWith(groups[groupId].Peers);
            }
        }

        internal void ForgetEntity(INetworkEntity entity)
        {
            if (!groupsOfEntity.Remove(entity, out List<uint> entityGroups))
            {
                return;
            }

            foreach (uint groupId in entityGroups)
            {
                Members members = groups[groupId];
                members.Entities.Remove(entity);
                DropIfEmpty(groupId, members);
            }
        }

        internal void ForgetPeer(ulong peerId)
        {
            if (!groupsOfPeer.Remove(peerId, out List<uint> peerGroups))
            {
                return;
            }

            foreach (uint groupId in peerGroups)
            {
                Members members = groups[groupId];
                members.Peers.Remove(peerId);
                DropIfEmpty(groupId, members);
            }
        }

        internal void Clear()
        {
            groups.Clear();
            groupsOfPeer.Clear();
            groupsOfEntity.Clear();
        }

        private static List<uint> ListOf<TKey>(Dictionary<TKey, List<uint>> lists, TKey key)
        {
            if (!lists.TryGetValue(key, out List<uint> list))
            {
                list = new List<uint>();
                lists.Add(key, list);
            }

            return list;
        }

        private static void RemoveFrom<TKey>(Dictionary<TKey, List<uint>> lists, TKey key, uint groupId)
        {
            List<uint> list = lists[key];
            list.Remove(groupId);
            if (list.Count == 0)
            {
                lists.Remove(key);
            }
        }

        private Members MembersOf(uint groupId)
        {
            if (!groups.TryGetValue(groupId, out Members members))
            {
                members = new Members();
                groups.Add(groupId, members);
            }

            return members;
        }

        private void DropIfEmpty(uint groupId, Members members)
        {
            if (members.Peers.Count == 0 && members.Entities.Count == 0)
            {
                groups.Remove(groupId);
            }
        }

        private void RebuildEntitiesOf(Members members, ulong peerId)
        {
            if (!entities.Objects.PeerStarted(peerId))
            {
                return;
            }

            affected.Clear();
            foreach (INetworkEntity entity in members.Entities)
            {
                EntityRecord record = entity.Record;
                if (record != null && record.Server == entities)
                {
                    affected.Add(record.ObjectId);
                }
            }

            entities.Objects.RebuildPairs(affected, peerId);
            affected.Clear();
        }

        private void RebuildEntity(INetworkEntity entity)
        {
            EntityRecord record = entity.Record;
            if (record != null && record.Server == entities)
            {
                record.GroupIds = GroupIdsOf(entity);
                entities.Objects.RebuildObservers(record.ObjectId);
            }
        }

        private sealed class Members
        {
            public HashSet<ulong> Peers { get; } = new HashSet<ulong>();

            public HashSet<INetworkEntity> Entities { get; } = new HashSet<INetworkEntity>();
        }
    }
}
