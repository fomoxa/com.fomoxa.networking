using System;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Networking.Objects
{
    public sealed class StateProtocol
    {
        public StateProtocol(IMessageCodec<StateDelta> deltaCodec, IMessageCodec<StateResync> resyncCodec, IMessageCodec<AnimatorState> animatorCodec, CollectionProtocol collections, MessageChannels channels)
        {
            DeltaCodec = deltaCodec ?? throw new ArgumentNullException(nameof(deltaCodec));
            ResyncCodec = resyncCodec ?? throw new ArgumentNullException(nameof(resyncCodec));
            AnimatorCodec = animatorCodec ?? throw new ArgumentNullException(nameof(animatorCodec));
            Collections = collections ?? throw new ArgumentNullException(nameof(collections));
            if (channels == null)
            {
                throw new ArgumentNullException(nameof(channels));
            }

            ObjectProtocol.EnsureReliable(channels, deltaCodec.MessageId, nameof(StateDelta));
            ObjectProtocol.EnsureReliable(channels, resyncCodec.MessageId, nameof(StateResync));
            ObjectProtocol.EnsureReliable(channels, animatorCodec.MessageId, nameof(AnimatorState));
            ObjectProtocol.EnsureReliable(channels, collections.ChangesCodec.MessageId, nameof(CollectionChanges));
            ObjectProtocol.EnsureReliable(channels, collections.ContentCodec.MessageId, nameof(CollectionContent));
            ObjectProtocol.EnsureReliable(channels, collections.ResyncCodec.MessageId, nameof(CollectionResync));
        }

        public IMessageCodec<StateDelta> DeltaCodec { get; }

        public IMessageCodec<StateResync> ResyncCodec { get; }

        public IMessageCodec<AnimatorState> AnimatorCodec { get; }

        public CollectionProtocol Collections { get; }

        public static StateProtocol From(FomoxaRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            return new StateProtocol(registry.Codec<StateDelta>(), registry.Codec<StateResync>(), registry.Codec<AnimatorState>(), CollectionProtocol.From(registry), registry.Channels);
        }
    }
}
