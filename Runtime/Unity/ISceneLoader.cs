using System;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity
{
    public interface ISceneLoader
    {
        void Load(Action<Scene> loaded, Action<Exception> failed);

        void Unload(Scene scene, Action unloaded);
    }
}
