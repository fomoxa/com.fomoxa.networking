using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity.Editor
{
    public static class FomoxaSceneObjectIds
    {
        private static readonly System.Random Random = new System.Random();

        public static uint SceneHashOf(string sceneGuid) => string.IsNullOrEmpty(sceneGuid) ? 0 : PrefabHash.Fnv1a(sceneGuid);

        public static bool Assign(Scene scene, uint sceneHash)
        {
            bool changed = false;
            var used = new HashSet<uint>();
            foreach (NetworkObject networkObject in NetworkObjectsIn(scene))
            {
                uint localId = (uint)networkObject.SceneObjectId;
                if (localId == 0 || !used.Add(localId))
                {
                    localId = NewLocalId(used);
                }

                changed |= Write(networkObject, ((ulong)sceneHash << 32) | localId);
            }

            return changed;
        }

        public static void SetSceneHash(Scene scene, uint sceneHash)
        {
            foreach (NetworkObject networkObject in NetworkObjectsIn(scene))
            {
                uint localId = (uint)networkObject.SceneObjectId;
                if (localId != 0)
                {
                    Write(networkObject, ((ulong)sceneHash << 32) | localId);
                }
            }
        }

        public static List<string> Validate(Scene scene)
        {
            var errors = new List<string>();
            var owners = new Dictionary<uint, string>();
            foreach (NetworkObject networkObject in NetworkObjectsIn(scene))
            {
                string name = PathOf(networkObject.transform);
                if (!PrefabHash.TryDescribeSceneObject(networkObject, out uint _, out string error))
                {
                    errors.Add($"{scene.path}: {name}: {error}");
                    continue;
                }

                uint localId = (uint)networkObject.SceneObjectId;
                if (localId == 0)
                {
                    errors.Add($"{scene.path}: {name} has no scene object id; save the scene");
                    continue;
                }

                if (owners.TryGetValue(localId, out string owner))
                {
                    errors.Add($"{scene.path}: {name} has the scene object id of {owner}; save the scene");
                    continue;
                }

                owners.Add(localId, name);
            }

            return errors;
        }

        public static void ProcessScene(Scene scene, bool isBuild)
        {
            uint sceneHash = SceneHashOf(AssetDatabase.AssetPathToGUID(scene.path));
            SetSceneHash(scene, sceneHash);
            List<string> errors = Validate(scene);
            if (errors.Count == 0)
            {
                return;
            }

            if (isBuild)
            {
                throw new UnityEditor.Build.BuildFailedException("[Fomoxa] " + string.Join("\n", errors));
            }

            if (Assign(scene, sceneHash))
            {
                Debug.LogWarning($"[Fomoxa] {scene.path}: scene objects without a unique scene object id got one for this Play Mode session only; save the scene so every build uses the same ids");
            }

            foreach (string remaining in Validate(scene))
            {
                Debug.LogError("[Fomoxa] " + remaining);
            }
        }

        private static IEnumerable<NetworkObject> NetworkObjectsIn(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (NetworkObject networkObject in root.GetComponentsInChildren<NetworkObject>(true))
                {
                    yield return networkObject;
                }
            }
        }

        private static uint NewLocalId(HashSet<uint> used)
        {
            var bytes = new byte[4];
            uint localId;
            do
            {
                Random.NextBytes(bytes);
                localId = BitConverter.ToUInt32(bytes, 0);
            }
            while (localId == 0 || !used.Add(localId));

            return localId;
        }

        private static bool Write(NetworkObject networkObject, ulong sceneObjectId)
        {
            if (networkObject.SceneObjectId == sceneObjectId)
            {
                return false;
            }

            var serialized = new SerializedObject(networkObject);
            serialized.FindProperty("sceneObjectId").ulongValue = sceneObjectId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static string PathOf(Transform transform) =>
            transform.parent == null ? transform.name : PathOf(transform.parent) + "/" + transform.name;
    }
}
