using System;
using System.Collections.Generic;

namespace Fomoxa.Networking.Objects
{
    public interface IServerSceneHost
    {
        bool TryLoad(uint sceneId, Func<bool> accept, Action loaded, Action<Exception> failed);

        void Unload(uint sceneId, Action unloaded);

        void SceneObjectsOf(uint sceneId, List<INetworkEntity> found);

        bool Holds(uint sceneId, INetworkEntity entity);

        bool Knows(uint sceneId);

        void PresentSceneObjects(List<INetworkEntity> found);
    }
}
