using System.Collections.Generic;
using Fomoxa.Networking;
using UnityEngine;

namespace Fomoxa.Unity
{
    [CreateAssetMenu(menuName = "Fomoxa/Distance Rule", fileName = "DistanceRule")]
    public sealed class DistanceRule : ObserverRule
    {
        [SerializeField] private float radius = 50f;

        public float Radius
        {
            get => radius;
            set => radius = Mathf.Max(0f, value);
        }

        internal override bool RebuildsOnFirstAnchor => true;

        public override bool Observes(NetworkObject networkObject, ulong peerId) =>
            networkObject != null && networkObject.Server != null && Decide(networkObject.Server, networkObject, peerId);

        internal override bool Decide(ServerManager server, NetworkObject networkObject, ulong peerId)
        {
            IReadOnlyList<System.Numerics.Vector3> anchors = server.AnchorsOf(peerId);
            if (anchors.Count == 0)
            {
                return false;
            }

            float range = networkObject.Range != null ? networkObject.Range.Radius : radius;
            float limit = range * range;
            System.Numerics.Vector3 position = ((INetworkEntity)networkObject).ReadWorldPosition();
            for (int index = 0; index < anchors.Count; index++)
            {
                if ((anchors[index] - position).LengthSquared() <= limit)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
