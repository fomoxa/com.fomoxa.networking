using System;
using Fomoxa.Networking;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity
{
    public static class NetworkScenesExtensions
    {
        public static bool TryGetScene(this NetworkScenes scenes, uint sceneId, out Scene scene)
        {
            if (scenes == null)
            {
                throw new ArgumentNullException(nameof(scenes));
            }

            if (scenes.Server.SceneHost is UnityServerSceneHost host)
            {
                return host.TryGetScene(sceneId, out scene);
            }

            scene = default;
            return false;
        }
    }
}
