using Fomoxa.Unity.Tests.Support;
using UnityEditor;
using UnityEngine;

namespace Fomoxa.Unity.Tests
{
    internal static class TestPrefabs
    {
        public static NetworkObject Create(string name, uint prefabId, bool explicitPrefabId = false)
        {
            var gameObject = new GameObject(name);
            NetworkObject networkObject = gameObject.AddComponent<NetworkObject>();
            gameObject.AddComponent<RecordingBehaviour>();
            SetPrefabId(networkObject, prefabId, explicitPrefabId);
            return networkObject;
        }

        public static void SetPrefabId(NetworkObject networkObject, uint prefabId, bool explicitPrefabId = false)
        {
            var serialized = new SerializedObject(networkObject);
            serialized.FindProperty("prefabId").uintValue = prefabId;
            serialized.FindProperty("explicitPrefabId").boolValue = explicitPrefabId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static NetworkObject CreateSceneObject(string name, ulong sceneObjectId, uint prefabId = 0)
        {
            NetworkObject networkObject = Create(name, prefabId);
            var serialized = new SerializedObject(networkObject);
            serialized.FindProperty("sceneObjectId").ulongValue = sceneObjectId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return networkObject;
        }

        public static int CountInScene() =>
            Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

        public static void DestroyAllInScene()
        {
            foreach (NetworkObject networkObject in Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (networkObject != null)
                {
                    Object.DestroyImmediate(networkObject.transform.root.gameObject);
                }
            }
        }
    }
}
