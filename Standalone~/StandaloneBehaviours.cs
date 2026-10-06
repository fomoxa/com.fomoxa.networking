using System;
using System.Collections.Generic;

namespace Fomoxa.Networking.Standalone
{
    public sealed class StandaloneBehaviours
    {
        private readonly Dictionary<string, Func<EntityBehaviour>> factories = new Dictionary<string, Func<EntityBehaviour>>(StringComparer.Ordinal);

        public void Register(string typeName, Func<EntityBehaviour> create)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                throw new ArgumentException("a behaviour type needs its full name", nameof(typeName));
            }

            if (create == null)
            {
                throw new ArgumentNullException(nameof(create));
            }

            if (factories.TryGetValue(typeName, out Func<EntityBehaviour> existing))
            {
                if (existing == create)
                {
                    return;
                }

                throw new ArgumentException($"behaviour type {typeName} is already registered with another factory", nameof(typeName));
            }

            factories.Add(typeName, create);
        }

        internal EntityBehaviour Create(string typeName)
        {
            if (!factories.TryGetValue(typeName, out Func<EntityBehaviour> create))
            {
                throw new InvalidOperationException($"behaviour type {typeName} is not registered with StandaloneBehaviours");
            }

            return create()
                ?? throw new InvalidOperationException($"the factory of behaviour type {typeName} returned no behaviour");
        }
    }
}
