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

        public ulong OwnerId
        {
            get
            {
                if (Server != null && Server.Objects.TryGet(ObjectId, out ObjectRow row))
                {
                    return row.OwnerId;
                }

                if (Client != null && Client.Objects.TryGet(ObjectId, out row))
                {
                    return row.OwnerId;
                }

                return 0;
            }
        }

        public bool IsOwner => Client != null && Client.Objects.IsOwner(ObjectId);

        public bool OnServer => Server != null;

        public bool OnClient => Client != null;

        internal ServerEntities Server { get; set; }

        internal ClientEntities Client { get; set; }

        internal uint Fingerprint { get; }

        internal bool HasInput { get; set; }

        internal LinkedListNode<EntityRecord> SpawnOrderNode { get; set; }
    }
}
