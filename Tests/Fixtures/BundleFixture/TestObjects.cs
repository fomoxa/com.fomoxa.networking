using System.Collections.Generic;
using Fomoxa.Net;
using Fomoxa.Networking;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Prediction;
using Fomoxa.Networking.Timing;

namespace BundleFixture
{
    public static class TestObjects
    {
        public const uint GreetingId = 0x2000_0001;

        public static uint LocalPeerId => LocalPeerNetAdapter.Instance.MessageId;

        public static uint SpawnId => ObjectSpawnNetAdapter.Instance.MessageId;

        public static uint SceneSpawnId => ObjectSceneSpawnNetAdapter.Instance.MessageId;

        public static uint DespawnId => ObjectDespawnNetAdapter.Instance.MessageId;

        public static uint OwnerChangeId => ObjectOwnerChangeNetAdapter.Instance.MessageId;

        public static uint StateDeltaId => StateDeltaNetAdapter.Instance.MessageId;

        public static uint StateResyncId => StateResyncNetAdapter.Instance.MessageId;

        public static uint TransformUpdateId => TransformUpdateNetAdapter.Instance.MessageId;

        public static uint TransformSettleId => TransformSettleNetAdapter.Instance.MessageId;

        public static uint AnimatorStateId => AnimatorStateNetAdapter.Instance.MessageId;

        public static uint SceneLoadId => SceneLoadNetAdapter.Instance.MessageId;

        public static uint SceneUnloadId => SceneUnloadNetAdapter.Instance.MessageId;

        public static uint SceneLoadedId => SceneLoadedNetAdapter.Instance.MessageId;

        public static uint TickPingId => TickPingNetAdapter.Instance.MessageId;

        public static uint TickPongId => TickPongNetAdapter.Instance.MessageId;

        public static uint InputFramesId => InputFramesNetAdapter.Instance.MessageId;

        public static uint ReconcileStateId => ReconcileStateNetAdapter.Instance.MessageId;

        public static Schema Schema() => SchemaWith(new MessageSchema(GreetingId, 0xF00D, new ulong[] { 0xF00D }));

        public static Schema SchemaWith(params MessageSchema[] messages)
        {
            var all = new List<MessageSchema>(messages)
            {
                new MessageSchema(Handshake.LocalPeerNetMessageId, Handshake.LocalPeerNetFingerprint, Handshake.LocalPeerNetPrefixes),
                new MessageSchema(Handshake.ObjectSpawnNetMessageId, Handshake.ObjectSpawnNetFingerprint, Handshake.ObjectSpawnNetPrefixes),
                new MessageSchema(Handshake.ObjectSceneSpawnNetMessageId, Handshake.ObjectSceneSpawnNetFingerprint, Handshake.ObjectSceneSpawnNetPrefixes),
                new MessageSchema(Handshake.ObjectDespawnNetMessageId, Handshake.ObjectDespawnNetFingerprint, Handshake.ObjectDespawnNetPrefixes),
                new MessageSchema(Handshake.ObjectOwnerChangeNetMessageId, Handshake.ObjectOwnerChangeNetFingerprint, Handshake.ObjectOwnerChangeNetPrefixes),
                new MessageSchema(Handshake.StateDeltaNetMessageId, Handshake.StateDeltaNetFingerprint, Handshake.StateDeltaNetPrefixes),
                new MessageSchema(Handshake.StateResyncNetMessageId, Handshake.StateResyncNetFingerprint, Handshake.StateResyncNetPrefixes),
                new MessageSchema(Handshake.TransformUpdateNetMessageId, Handshake.TransformUpdateNetFingerprint, Handshake.TransformUpdateNetPrefixes),
                new MessageSchema(Handshake.TransformSettleNetMessageId, Handshake.TransformSettleNetFingerprint, Handshake.TransformSettleNetPrefixes),
                new MessageSchema(Handshake.AnimatorStateNetMessageId, Handshake.AnimatorStateNetFingerprint, Handshake.AnimatorStateNetPrefixes),
                new MessageSchema(Handshake.SceneLoadNetMessageId, Handshake.SceneLoadNetFingerprint, Handshake.SceneLoadNetPrefixes),
                new MessageSchema(Handshake.SceneUnloadNetMessageId, Handshake.SceneUnloadNetFingerprint, Handshake.SceneUnloadNetPrefixes),
                new MessageSchema(Handshake.SceneLoadedNetMessageId, Handshake.SceneLoadedNetFingerprint, Handshake.SceneLoadedNetPrefixes),
                new MessageSchema(Handshake.TickPingNetMessageId, Handshake.TickPingNetFingerprint, Handshake.TickPingNetPrefixes),
                new MessageSchema(Handshake.TickPongNetMessageId, Handshake.TickPongNetFingerprint, Handshake.TickPongNetPrefixes),
                new MessageSchema(Handshake.InputFramesNetMessageId, Handshake.InputFramesNetFingerprint, Handshake.InputFramesNetPrefixes),
                new MessageSchema(Handshake.ReconcileStateNetMessageId, Handshake.ReconcileStateNetFingerprint, Handshake.ReconcileStateNetPrefixes),
                new MessageSchema(Handshake.PeerLeaveNetMessageId, Handshake.PeerLeaveNetFingerprint, Handshake.PeerLeaveNetPrefixes),
                new MessageSchema(Handshake.SceneFileNetMessageId, Handshake.SceneFileNetFingerprint, Handshake.SceneFileNetPrefixes),
            };
            return new Schema(0xCAFE, all.ToArray());
        }

        public static FomoxaRegistry Registry(params MessageSchema[] messages)
        {
            var registry = new FomoxaRegistry();
            registry.SetSchema(SchemaWith(messages));
            registry.SetCodec(MessageBundleNetAdapter.Instance);
            registry.SetCodec(ReliableAckNetAdapter.Instance);
            registry.SetCodec(LocalPeerNetAdapter.Instance);
            registry.SetCodec(ObjectSpawnNetAdapter.Instance);
            registry.SetCodec(ObjectSceneSpawnNetAdapter.Instance);
            registry.SetCodec(ObjectDespawnNetAdapter.Instance);
            registry.SetCodec(ObjectOwnerChangeNetAdapter.Instance);
            registry.SetCodec(StateDeltaNetAdapter.Instance);
            registry.SetCodec(StateResyncNetAdapter.Instance);
            registry.SetCodec(TransformUpdateNetAdapter.Instance);
            registry.SetCodec(TransformSettleNetAdapter.Instance);
            registry.SetCodec(AnimatorStateNetAdapter.Instance);
            registry.SetCodec(SceneLoadNetAdapter.Instance);
            registry.SetCodec(SceneUnloadNetAdapter.Instance);
            registry.SetCodec(SceneLoadedNetAdapter.Instance);
            registry.SetCodec(TickPingNetAdapter.Instance);
            registry.SetCodec(TickPongNetAdapter.Instance);
            registry.SetCodec(InputFramesNetAdapter.Instance);
            registry.SetCodec(ReconcileStateNetAdapter.Instance);
            registry.SetCodec(PeerLeaveNetAdapter.Instance);
            registry.SetCodec(SceneFileNetAdapter.Instance);
            registry.Channels.Set(LocalPeerId, Channel.ReliableOrdered);
            registry.Channels.Set(SpawnId, Channel.ReliableOrdered);
            registry.Channels.Set(SceneSpawnId, Channel.ReliableOrdered);
            registry.Channels.Set(DespawnId, Channel.ReliableOrdered);
            registry.Channels.Set(OwnerChangeId, Channel.ReliableOrdered);
            registry.Channels.Set(StateDeltaId, Channel.ReliableOrdered);
            registry.Channels.Set(StateResyncId, Channel.ReliableOrdered);
            registry.Channels.Set(TransformSettleId, Channel.ReliableOrdered);
            registry.Channels.Set(AnimatorStateId, Channel.ReliableOrdered);
            registry.Channels.Set(SceneLoadId, Channel.ReliableOrdered);
            registry.Channels.Set(SceneUnloadId, Channel.ReliableOrdered);
            registry.Channels.Set(SceneLoadedId, Channel.ReliableOrdered);
            return registry;
        }

        public static MessageChannels Channels()
        {
            var channels = new MessageChannels();
            channels.Set(LocalPeerId, Channel.ReliableOrdered);
            channels.Set(SpawnId, Channel.ReliableOrdered);
            channels.Set(SceneSpawnId, Channel.ReliableOrdered);
            channels.Set(DespawnId, Channel.ReliableOrdered);
            channels.Set(OwnerChangeId, Channel.ReliableOrdered);
            channels.Set(StateDeltaId, Channel.ReliableOrdered);
            channels.Set(StateResyncId, Channel.ReliableOrdered);
            channels.Set(TransformSettleId, Channel.ReliableOrdered);
            channels.Set(AnimatorStateId, Channel.ReliableOrdered);
            channels.Set(SceneLoadId, Channel.ReliableOrdered);
            channels.Set(SceneUnloadId, Channel.ReliableOrdered);
            channels.Set(SceneLoadedId, Channel.ReliableOrdered);
            return channels;
        }

        public static ObjectProtocol Protocol(MessageChannels channels) =>
            new ObjectProtocol(
                LocalPeerNetAdapter.Instance,
                ObjectSpawnNetAdapter.Instance,
                ObjectSceneSpawnNetAdapter.Instance,
                ObjectDespawnNetAdapter.Instance,
                ObjectOwnerChangeNetAdapter.Instance,
                channels);

        public static StateProtocol StateProtocol(MessageChannels channels) =>
            new StateProtocol(StateDeltaNetAdapter.Instance, StateResyncNetAdapter.Instance, AnimatorStateNetAdapter.Instance, channels);

        public static SceneProtocol SceneProtocol(MessageChannels channels) =>
            new SceneProtocol(SceneLoadNetAdapter.Instance, SceneUnloadNetAdapter.Instance, SceneLoadedNetAdapter.Instance, channels);

        public static TransformProtocol TransformProtocol(MessageChannels channels) =>
            new TransformProtocol(TransformUpdateNetAdapter.Instance, TransformSettleNetAdapter.Instance, channels);

        public static ClockProtocol ClockProtocol() =>
            new ClockProtocol(TickPingNetAdapter.Instance, TickPongNetAdapter.Instance);

        public static InputProtocol InputProtocol() =>
            new InputProtocol(InputFramesNetAdapter.Instance, ReconcileStateNetAdapter.Instance);

        public static byte[] Spawn(uint objectId, uint prefabId, ulong ownerId, byte mask = 0, params float[] values)
        {
            var spawn = new ObjectSpawn { ObjectId = objectId, PrefabId = prefabId, OwnerId = ownerId, Mask = mask };
            spawn.Values.AddRange(values);
            return ObjectSpawnNetAdapter.Instance.Encode(spawn).ToArray();
        }

        public static byte[] SceneSpawn(uint objectId, ulong sceneObjectId, uint fingerprint, ulong ownerId, byte mask = 0, params float[] values)
        {
            var spawn = new ObjectSceneSpawn { ObjectId = objectId, SceneObjectId = sceneObjectId, Fingerprint = fingerprint, OwnerId = ownerId, Mask = mask };
            spawn.Values.AddRange(values);
            return ObjectSceneSpawnNetAdapter.Instance.Encode(spawn).ToArray();
        }

        public static byte[] Despawn(uint objectId) =>
            ObjectDespawnNetAdapter.Instance.Encode(new ObjectDespawn { ObjectId = objectId }).ToArray();

        public static byte[] OwnerChange(uint objectId, ulong ownerId) =>
            ObjectOwnerChangeNetAdapter.Instance.Encode(new ObjectOwnerChange { ObjectId = objectId, OwnerId = ownerId }).ToArray();
    }
}
