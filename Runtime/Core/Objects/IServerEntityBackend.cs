namespace Fomoxa.Networking.Objects
{
    public interface IServerEntityBackend
    {
        string NameOf(INetworkEntity entity);

        void ValidateSpawn(INetworkEntity entity);

        uint FingerprintOf(INetworkEntity entity, bool isSceneObject);

        uint SceneIdOf(INetworkEntity entity);

        void PrepareSpawn(INetworkEntity entity);

        void Activate(INetworkEntity entity);

        void End(INetworkEntity entity, bool isSceneObject);
    }
}
