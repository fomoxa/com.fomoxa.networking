using System;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Networking.Objects
{
    public sealed class SceneProtocol
    {
        public SceneProtocol(IMessageCodec<SceneLoad> loadCodec, IMessageCodec<SceneUnload> unloadCodec, IMessageCodec<SceneLoaded> loadedCodec, MessageChannels channels)
        {
            LoadCodec = loadCodec ?? throw new ArgumentNullException(nameof(loadCodec));
            UnloadCodec = unloadCodec ?? throw new ArgumentNullException(nameof(unloadCodec));
            LoadedCodec = loadedCodec ?? throw new ArgumentNullException(nameof(loadedCodec));
            if (channels == null)
            {
                throw new ArgumentNullException(nameof(channels));
            }

            ObjectProtocol.EnsureReliable(channels, loadCodec.MessageId, nameof(SceneLoad));
            ObjectProtocol.EnsureReliable(channels, unloadCodec.MessageId, nameof(SceneUnload));
            ObjectProtocol.EnsureReliable(channels, loadedCodec.MessageId, nameof(SceneLoaded));
        }

        public IMessageCodec<SceneLoad> LoadCodec { get; }

        public IMessageCodec<SceneUnload> UnloadCodec { get; }

        public IMessageCodec<SceneLoaded> LoadedCodec { get; }
    }
}
