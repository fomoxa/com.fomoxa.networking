using System.Text;
using UnityEngine;

namespace Fomoxa.Unity
{
    internal static class PrefabHash
    {
        public const int MaxBehaviours = 256;

        private const uint OffsetBasis = 2166136261;
        private const uint Prime = 16777619;

        public static uint Fnv1a(string text)
        {
            uint hash = OffsetBasis;
            foreach (byte value in Encoding.UTF8.GetBytes(text))
            {
                hash = unchecked((hash ^ value) * Prime);
            }

            return hash;
        }

        public static bool TryDescribe(NetworkObject prefab, out uint fingerprint, out string error)
        {
            fingerprint = 0;
            if (prefab.transform.parent != null)
            {
                error = "the NetworkObject is not on the root of the prefab";
                return false;
            }

            return TryDescribeHierarchy(prefab, out fingerprint, out error);
        }

        public static bool TryDescribeSceneObject(NetworkObject sceneObject, out uint fingerprint, out string error)
        {
            Transform parent = sceneObject.transform.parent;
            if (parent != null && parent.GetComponentInParent<NetworkObject>(true) != null)
            {
                fingerprint = 0;
                error = "the scene object is inside another NetworkObject; nested NetworkObjects are not supported";
                return false;
            }

            return TryDescribeHierarchy(sceneObject, out fingerprint, out error);
        }

        private static bool TryDescribeHierarchy(NetworkObject networkObject, out uint fingerprint, out string error)
        {
            fingerprint = 0;
            if (networkObject.GetComponentsInChildren<NetworkObject>(true).Length > 1)
            {
                error = "the object has a nested NetworkObject; nested NetworkObjects are not supported";
                return false;
            }

            NetworkBehaviour[] behaviours = networkObject.GetComponentsInChildren<NetworkBehaviour>(true);
            if (behaviours.Length > MaxBehaviours)
            {
                error = $"{behaviours.Length} NetworkBehaviours exceed the limit of {MaxBehaviours}";
                return false;
            }

            var typeNames = new string[behaviours.Length];
            for (int index = 0; index < behaviours.Length; index++)
            {
                typeNames[index] = behaviours[index].GetType().FullName;
            }

            fingerprint = Fnv1a(string.Join("\n", typeNames));
            error = null;
            return true;
        }
    }
}
