using System.Numerics;

namespace Fomoxa.Networking
{
    public readonly struct TransformSample
    {
        public TransformSample(uint tick, byte mask, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, bool settle, byte generation)
        {
            Tick = tick;
            Mask = mask;
            LocalPosition = localPosition;
            LocalRotation = localRotation;
            LocalScale = localScale;
            Settle = settle;
            Generation = generation;
        }

        public uint Tick { get; }

        public byte Mask { get; }

        public Vector3 LocalPosition { get; }

        public Quaternion LocalRotation { get; }

        public Vector3 LocalScale { get; }

        public bool Settle { get; }

        public byte Generation { get; }
    }
}
