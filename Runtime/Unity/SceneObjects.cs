using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity
{
    internal static class SceneObjects
    {
        public static IReadOnlyList<NetworkObject> InLoadedScenes()
        {
            var found = new List<NetworkObject>();
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                AddFrom(SceneManager.GetSceneAt(index), found);
            }

            return found;
        }

        public static void AddFrom(Scene scene, List<NetworkObject> found)
        {
            if (!scene.isLoaded)
            {
                return;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (NetworkObject networkObject in root.GetComponentsInChildren<NetworkObject>(true))
                {
                    if (networkObject.SceneObjectId != 0)
                    {
                        found.Add(networkObject);
                    }
                }
            }
        }
    }
}
