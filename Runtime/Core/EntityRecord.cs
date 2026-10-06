using System.Collections.Generic;
using Fomoxa.Networking.Objects;

namespace Fomoxa.Networking
{
    public sealed class EntityRecord
    {
        internal EntityRecord(INetworkEntity representation, uint fingerprint)
        {
            Representation = representation;
            Fingerprint = fingerprint;
        }

        public uint ObjectId { get; internal set; }

        public INetworkEntity Representation { get; }

        public ulong OwnerId => Server != null && Server.Objects.TryGet(ObjectId, out ObjectRow row) ? row.OwnerId : 0;

        public bool OnServer => Server != null;

        internal ServerEntities Server { get; set; }

        internal uint Fingerprint { get; }

        internal bool HasInput { get; set; }

        internal LinkedListNode<EntityRecord> SpawnOrderNode { get; set; }
    }
}
