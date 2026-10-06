namespace Fomoxa.Networking.Standalone
{
    public interface ISceneFiles
    {
        bool Knows(uint sceneId);

        byte[] Read(uint sceneId);
    }
}
