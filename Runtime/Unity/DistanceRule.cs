using Fomoxa.Networking;
using UnityEngine;

namespace Fomoxa.Unity
{
    [CreateAssetMenu(menuName = "Fomoxa/Distance Rule", fileName = "DistanceRule")]
    public sealed class DistanceRule : ObserverRule
    {
        [SerializeField] private float radius = 50f;

        private RangeRule rule;

        public float Radius
        {
            get => radius;
            set => radius = Mathf.Max(0f, value);
        }

        internal override bool RebuildsOnFirstAnchor => Rule.RebuildsOnFirstAnchor;

        private RangeRule Rule => rule ??= new RangeRule(this);

        public override bool Observes(NetworkObject networkObject, ulong peerId)
        {
            EntityRecord record = networkObject != null ? ((INetworkEntity)networkObject).Record : null;
            return record != null && record.Server != null && Rule.Observes(record.Server.Observers, networkObject, peerId);
        }

        internal override bool ObservesEntity(ObserverContext context, INetworkEntity entity, ulong peerId) =>
            Rule.Observes(context, entity, peerId);

        private sealed class RangeRule : DistanceObserverRule
        {
            private readonly DistanceRule owner;

            public RangeRule(DistanceRule owner)
                : base(0f)
            {
                this.owner = owner;
            }

            protected override float RadiusOf(INetworkEntity entity)
            {
                ObserverRange range = ((NetworkObject)entity).Range;
                return range != null ? range.Radius : owner.radius;
            }
        }
    }
}
