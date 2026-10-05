using System;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Networking.Prediction
{
    public sealed class InputProtocol
    {
        public InputProtocol(IMessageCodec<InputFrames> framesCodec, IMessageCodec<ReconcileState> reconcileCodec)
        {
            FramesCodec = framesCodec ?? throw new ArgumentNullException(nameof(framesCodec));
            ReconcileCodec = reconcileCodec ?? throw new ArgumentNullException(nameof(reconcileCodec));
        }

        public IMessageCodec<InputFrames> FramesCodec { get; }

        public IMessageCodec<ReconcileState> ReconcileCodec { get; }

        public static InputProtocol From(FomoxaRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            return new InputProtocol(registry.Codec<InputFrames>(), registry.Codec<ReconcileState>());
        }
    }
}
