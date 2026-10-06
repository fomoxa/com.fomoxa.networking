namespace Fomoxa.Networking
{
    public readonly struct TransformSyncSettings
    {
        public TransformSyncSettings(bool syncPosition, bool syncRotation, bool syncScale, float positionThreshold, float rotationThreshold, float scaleThreshold)
        {
            SyncPosition = syncPosition;
            SyncRotation = syncRotation;
            SyncScale = syncScale;
            PositionThreshold = positionThreshold;
            RotationThreshold = rotationThreshold;
            ScaleThreshold = scaleThreshold;
        }

        public bool SyncPosition { get; }

        public bool SyncRotation { get; }

        public bool SyncScale { get; }

        public float PositionThreshold { get; }

        public float RotationThreshold { get; }

        public float ScaleThreshold { get; }
    }
}
