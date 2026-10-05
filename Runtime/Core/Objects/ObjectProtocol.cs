using System;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Networking.Objects
{
    public sealed class ObjectProtocol
    {
        public ObjectProtocol(
            IMessageCodec<LocalPeer> localPeerCodec,
            IMessageCodec<ObjectSpawn> spawnCodec,
            IMessageCodec<ObjectSceneSpawn> sceneSpawnCodec,
            IMessageCodec<ObjectDespawn> despawnCodec,
            IMessageCodec<ObjectOwnerChange> ownerChangeCodec,
            MessageChannels channels)
        {
            LocalPeerCodec = localPeerCodec ?? throw new ArgumentNullException(nameof(localPeerCodec));
            SpawnCodec = spawnCodec ?? throw new ArgumentNullException(nameof(spawnCodec));
            SceneSpawnCodec = sceneSpawnCodec ?? throw new ArgumentNullException(nameof(sceneSpawnCodec));
            DespawnCodec = despawnCodec ?? throw new ArgumentNullException(nameof(despawnCodec));
            OwnerChangeCodec = ownerChangeCodec ?? throw new ArgumentNullException(nameof(ownerChangeCodec));
            if (channels == null)
            {
                throw new ArgumentNullException(nameof(channels));
            }

            EnsureReliable(channels, localPeerCodec.MessageId, nameof(LocalPeer));
            EnsureReliable(channels, spawnCodec.MessageId, nameof(ObjectSpawn));
            EnsureReliable(channels, sceneSpawnCodec.MessageId, nameof(ObjectSceneSpawn));
            EnsureReliable(channels, despawnCodec.MessageId, nameof(ObjectDespawn));
            EnsureReliable(channels, ownerChangeCodec.MessageId, nameof(ObjectOwnerChange));
        }

        public IMessageCodec<LocalPeer> LocalPeerCodec { get; }

        public IMessageCodec<ObjectSpawn> SpawnCodec { get; }

        public IMessageCodec<ObjectSceneSpawn> SceneSpawnCodec { get; }

        public IMessageCodec<ObjectDespawn> DespawnCodec { get; }

        public IMessageCodec<ObjectOwnerChange> OwnerChangeCodec { get; }

        public static ObjectProtocol From(FomoxaRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            return new ObjectProtocol(
                registry.Codec<LocalPeer>(),
                registry.Codec<ObjectSpawn>(),
                registry.Codec<ObjectSceneSpawn>(),
                registry.Codec<ObjectDespawn>(),
                registry.Codec<ObjectOwnerChange>(),
                registry.Channels);
        }

        internal static void EnsureReliable(MessageChannels channels, uint messageId, string model)
        {
            if (channels.Of(messageId) != Channel.ReliableOrdered)
            {
                throw new ArgumentException($"{model}.net must be on the reliable-ordered channel", nameof(channels));
            }
        }
    }
}
