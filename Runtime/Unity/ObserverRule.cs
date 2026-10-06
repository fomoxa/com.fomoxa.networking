using Fomoxa.Networking;
using UnityEngine;

namespace Fomoxa.Unity
{
    public abstract class ObserverRule : ScriptableObject, IObserverRule
    {
        bool IObserverRule.RebuildsOnFirstAnchor => this != null && RebuildsOnFirstAnchor;

        internal virtual bool RebuildsOnFirstAnchor => false;

        public abstract bool Observes(NetworkObject networkObject, ulong peerId);

        bool IObserverRule.Observes(ObserverContext context, INetworkEntity entity, ulong peerId) =>
            this == null || ObservesEntity(context, entity, peerId);

        internal virtual bool ObservesEntity(ObserverContext context, INetworkEntity entity, ulong peerId) =>
            Observes((NetworkObject)entity, peerId);
    }
}
