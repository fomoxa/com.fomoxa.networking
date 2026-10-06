using Fomoxa.Networking;
using UnityEngine;

namespace Fomoxa.Unity
{
    public abstract class ObserverRule : ScriptableObject
    {
        private Adapter adapter;

        public abstract bool Observes(NetworkObject networkObject, ulong peerId);

        internal IObserverRule CoreRule => adapter ??= new Adapter(this);

        internal virtual bool RebuildsOnFirstAnchor => false;

        internal virtual bool ObservesEntity(ObserverContext context, INetworkEntity entity, ulong peerId) =>
            Observes((NetworkObject)entity, peerId);

        private sealed class Adapter : IObserverRule
        {
            private readonly ObserverRule owner;

            public Adapter(ObserverRule owner)
            {
                this.owner = owner;
            }

            public bool RebuildsOnFirstAnchor => owner != null && owner.RebuildsOnFirstAnchor;

            public bool Observes(ObserverContext context, INetworkEntity entity, ulong peerId) =>
                owner == null || owner.ObservesEntity(context, entity, peerId);
        }
    }
}
