namespace Fomoxa.Networking.Objects
{
    public interface IClientEntityBackend
    {
        SpawnResult CheckPrefab(in SpawnedObject spawned);

        INetworkEntity Create(in SpawnedObject spawned);

        SpawnResult PlaceSceneObject(in SpawnedObject spawned, out INetworkEntity entity);

        void End(INetworkEntity entity);

        void HideOnHost(INetworkEntity entity);

        void ShowOnHost(INetworkEntity entity);

        void PrepareSceneObjects();
    }
}
