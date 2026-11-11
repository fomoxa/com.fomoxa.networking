using UnityEngine;

namespace Fomoxa.Unity
{
    public readonly struct TransformCorrection
    {
        public TransformCorrection(uint tick, Vector3 positionBefore, Vector3 positionAfter, Quaternion rotationBefore, Quaternion rotationAfter)
        {
            Tick = tick;
            PositionBefore = positionBefore;
            PositionAfter = positionAfter;
            RotationBefore = rotationBefore;
            RotationAfter = rotationAfter;
        }

        public uint Tick { get; }

        public Vector3 PositionBefore { get; }

        public Vector3 PositionAfter { get; }

        public Quaternion RotationBefore { get; }

        public Quaternion RotationAfter { get; }

        public Vector3 PositionOffset => PositionAfter - PositionBefore;

        public float RotationDegrees => Quaternion.Angle(RotationBefore, RotationAfter);
    }
}
