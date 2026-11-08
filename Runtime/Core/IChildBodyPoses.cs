using System.Numerics;

namespace Fomoxa.Networking
{
    internal interface IChildBodyPoses
    {
        void ReadChildPose(ChildBody body, out Vector3 localPosition, out Quaternion localRotation);
    }
}
