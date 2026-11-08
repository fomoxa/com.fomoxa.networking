using System;
using System.Numerics;

namespace Fomoxa.Networking
{
    internal sealed class ChildBodySource : ITransformSource
    {
        private readonly EntityBehaviour behaviour;
        private readonly ChildBody body;

        public ChildBodySource(EntityBehaviour behaviour, ChildBody body)
        {
            this.behaviour = behaviour;
            this.body = body;
        }

        public ChildBody Body => body;

        public void ReadLocal(out Vector3 localPosition, out Quaternion localRotation, out Vector3 localScale)
        {
            if (!(behaviour.TransformSlot.Record.Representation is IChildBodyPoses poses))
            {
                throw new InvalidOperationException($"{behaviour.GetType().FullName} synchronizes {body}, but its entity has no child bodies");
            }

            poses.ReadChildPose(body, out localPosition, out localRotation);
            localScale = Vector3.One;
        }
    }
}
