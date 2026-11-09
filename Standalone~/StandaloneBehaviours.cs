using System;
using System.Collections.Generic;

namespace Fomoxa.Networking.Standalone
{
    public sealed class StandaloneBehaviours
    {
        private readonly Dictionary<string, Delegate> factories = new Dictionary<string, Delegate>(StringComparer.Ordinal);

        public void Register(string typeName, Func<EntityBehaviour> create) => Add(typeName, create);

        public void Register(string typeName, Func<BehaviourSite, EntityBehaviour> create) => Add(typeName, create);

        internal EntityBehaviour Create(string typeName, BehaviourSite site)
        {
            if (!factories.TryGetValue(typeName, out Delegate factory))
            {
                throw new InvalidOperationException($"behaviour type {typeName} is not registered with StandaloneBehaviours");
            }

            EntityBehaviour created = factory is Func<BehaviourSite, EntityBehaviour> placed ? placed(site) : ((Func<EntityBehaviour>)factory)();
            return created ?? throw new InvalidOperationException($"the factory of behaviour type {typeName} returned no behaviour");
        }

        private void Add(string typeName, Delegate create)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                throw new ArgumentException("a behaviour type needs its full name", nameof(typeName));
            }

            if (create == null)
            {
                throw new ArgumentNullException(nameof(create));
            }

            if (factories.TryGetValue(typeName, out Delegate existing))
            {
                if (existing.Equals(create))
                {
                    return;
                }

                throw new ArgumentException($"behaviour type {typeName} is already registered with another factory", nameof(typeName));
            }

            factories.Add(typeName, create);
        }
    }
}
