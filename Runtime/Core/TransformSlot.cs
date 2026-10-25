namespace Fomoxa.Networking
{
    internal sealed class TransformSlot
    {
        public TransformSlot(TransformSync sync, ITransformSource source, ITransformReceiver receiver, TransformTarget target)
        {
            Sync = sync;
            Source = source;
            Receiver = receiver;
            Target = target;
        }

        public TransformSync Sync { get; }

        public ITransformSource Source { get; }

        public ITransformReceiver Receiver { get; }

        public TransformTarget Target { get; }

        public EntityRecord Record { get; set; }

        public bool Tracked { get; set; }

        public void Signal() => Record?.Server?.SignalTransform(Record);
    }
}
