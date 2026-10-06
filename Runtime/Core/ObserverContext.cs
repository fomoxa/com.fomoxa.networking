using System.Collections.Generic;
using System.Numerics;
using Fomoxa.Networking.Objects;

namespace Fomoxa.Networking
{
    public sealed class ObserverContext
    {
        private readonly ServerEntities entities;

        internal ObserverContext(ServerEntities entities)
        {
            this.entities = entities;
        }

        public IReadOnlyList<Vector3> AnchorsOf(ulong peerId) => entities.AnchorsOf(peerId);
    }
}
