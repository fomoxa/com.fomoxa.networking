namespace Fomoxa.Networking
{
    internal sealed class TransformSlot
    {
        public TransformSlot(TransformSync sync, ITransformSource source, ITransformReceiver receiver)
        {
            Sync = sync;
            Source = source;
            Receiver = receiver;
        }

        public TransformSync Sync { get; }

        public ITransformSource Source { get; }

        public ITransformReceiver Receiver { get; }
    }
}
