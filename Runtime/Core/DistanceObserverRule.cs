using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fomoxa.Networking
{
    public class DistanceObserverRule : IObserverRule
    {
        private float radius;

        public DistanceObserverRule(float radius)
        {
            Radius = radius;
        }

        public float Radius
        {
            get => radius;
            set => radius = Math.Max(0f, value);
        }

        public bool RebuildsOnFirstAnchor => true;

        public bool Observes(ObserverContext context, INetworkEntity entity, ulong peerId)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            IReadOnlyList<Vector3> anchors = context.AnchorsOf(peerId);
            if (anchors.Count == 0)
            {
                return false;
            }

            float range = RadiusOf(entity);
            float limit = range * range;
            Vector3 position = entity.ReadWorldPosition();
            for (int index = 0; index < anchors.Count; index++)
            {
                Vector3 anchor = anchors[index];
                float x = anchor.X - position.X;
                float y = anchor.Y - position.Y;
                float z = anchor.Z - position.Z;
                if ((x * x) + (y * y) + (z * z) <= limit)
                {
                    return true;
                }
            }

            return false;
        }

        protected virtual float RadiusOf(INetworkEntity entity) => radius;
    }
}
