namespace Fomoxa.Unity.Editor
{
    public readonly struct FomoxaScene
    {
        public FomoxaScene(string path, uint sceneId)
        {
            Path = path;
            SceneId = sceneId;
        }

        public string Path { get; }

        public uint SceneId { get; }
    }
}
