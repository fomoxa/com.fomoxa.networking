using System.Numerics;

namespace Fomoxa.Networking
{
    public interface ITransformSource
    {
        void ReadLocal(out Vector3 localPosition, out Quaternion localRotation, out Vector3 localScale);
    }
}
