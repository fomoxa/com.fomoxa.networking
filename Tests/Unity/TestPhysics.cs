using UnityEditor;
using UnityEngine;

namespace Fomoxa.Unity.Tests
{
    internal static class TestPhysics
    {
        public static RigidbodyPhysics Rigidbody(GameObject owner, bool simulate)
        {
            var physics = owner.AddComponent<RigidbodyPhysics>();
            var serialized = new SerializedObject(physics);
            serialized.FindProperty("simulatePhysics").boolValue = simulate;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return physics;
        }
    }
}
