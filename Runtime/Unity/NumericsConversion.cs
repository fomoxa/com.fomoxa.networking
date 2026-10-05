using UnityEngine;

namespace Fomoxa.Unity
{
    internal static class NumericsConversion
    {
        public static System.Numerics.Vector2 ToNumerics(this Vector2 value) =>
            new System.Numerics.Vector2(value.x, value.y);

        public static System.Numerics.Vector3 ToNumerics(this Vector3 value) =>
            new System.Numerics.Vector3(value.x, value.y, value.z);

        public static System.Numerics.Quaternion ToNumerics(this Quaternion value) =>
            new System.Numerics.Quaternion(value.x, value.y, value.z, value.w);

        public static Vector2 ToUnity(this System.Numerics.Vector2 value) =>
            new Vector2(value.X, value.Y);

        public static Vector3 ToUnity(this System.Numerics.Vector3 value) =>
            new Vector3(value.X, value.Y, value.Z);

        public static Quaternion ToUnity(this System.Numerics.Quaternion value) =>
            new Quaternion(value.X, value.Y, value.Z, value.W);
    }
}
