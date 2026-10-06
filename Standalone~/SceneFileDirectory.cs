using System;
using System.IO;
using Fomoxa.Networking.Objects;

namespace Fomoxa.Networking.Standalone
{
    public sealed class SceneFileDirectory : ISceneFiles
    {
        private readonly string directory;

        public SceneFileDirectory(string directory)
        {
            this.directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        public bool Knows(uint sceneId) => File.Exists(PathOf(sceneId));

        public byte[] Read(uint sceneId) => File.ReadAllBytes(PathOf(sceneId));

        private string PathOf(uint sceneId) => Path.Combine(directory, sceneId + SceneFileFormat.Extension);
    }
}
