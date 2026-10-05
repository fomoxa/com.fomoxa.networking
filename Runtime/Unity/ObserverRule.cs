using UnityEngine;

namespace Fomoxa.Unity
{
    public abstract class ObserverRule : ScriptableObject
    {
        internal virtual bool RebuildsOnFirstAnchor => false;

        public abstract bool Observes(NetworkObject networkObject, ulong peerId);

        internal virtual bool Decide(ServerManager server, NetworkObject networkObject, ulong peerId) => Observes(networkObject, peerId);
    }
}
