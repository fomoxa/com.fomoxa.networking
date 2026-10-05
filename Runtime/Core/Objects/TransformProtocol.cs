using System;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Networking.Objects
{
    public sealed class TransformProtocol
    {
        public TransformProtocol(IMessageCodec<TransformUpdate> updateCodec, IMessageCodec<TransformSettle> settleCodec, MessageChannels channels)
        {
            UpdateCodec = updateCodec ?? throw new ArgumentNullException(nameof(updateCodec));
            SettleCodec = settleCodec ?? throw new ArgumentNullException(nameof(settleCodec));
            if (channels == null)
            {
                throw new ArgumentNullException(nameof(channels));
            }

            ObjectProtocol.EnsureReliable(channels, settleCodec.MessageId, nameof(TransformSettle));
        }

        public IMessageCodec<TransformUpdate> UpdateCodec { get; }

        public IMessageCodec<TransformSettle> SettleCodec { get; }

        public static TransformProtocol From(FomoxaRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            return new TransformProtocol(registry.Codec<TransformUpdate>(), registry.Codec<TransformSettle>(), registry.Channels);
        }
    }
}
