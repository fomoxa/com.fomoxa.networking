using System.Numerics;

namespace Fomoxa.Networking
{
    internal sealed class EntityPoseSource : ITransformSource
    {
        private readonly EntityBehaviour behaviour;

        public EntityPoseSource(EntityBehaviour behaviour)
        {
            this.behaviour = behaviour;
        }

        public void ReadLocal(out Vector3 localPosition, out Quaternion localRotation, out Vector3 localScale) =>
            behaviour.TransformSlot.Record.Representation.ReadRootPose(out localPosition, out localRotation, out localScale);
    }
}
