using System;
using System.Numerics;
using Fomoxa.Networking.Objects;

namespace Fomoxa.Networking
{
    public sealed class TransformSync
    {
        private const float SameRotationDot = 1f - 0.000001f;
        private const float RadiansToDegrees = 360f / ((float)Math.PI * 2f);

        private EntityBehaviour behaviour;
        private Vector3 sentPosition;
        private Quaternion sentRotation;
        private Vector3 sentScale;
        private Vector3 sampledPosition;
        private Quaternion sampledRotation;
        private Vector3 sampledScale;
        private bool moving;

        public TransformSync(TransformSyncSettings settings)
        {
            Settings = settings;
        }

        public TransformSyncSettings Settings { get; set; }

        public byte Generation { get; private set; }

        internal bool SettlePending { get; set; }

        internal byte SelectedMask =>
            (byte)((Settings.SyncPosition ? SpawnTransform.PositionBit : 0)
                | (Settings.SyncRotation ? SpawnTransform.RotationBit : 0)
                | (Settings.SyncScale ? SpawnTransform.ScaleBit : 0));

        public void Teleport()
        {
            if (behaviour == null || !behaviour.SpawnedOnServer)
            {
                return;
            }

            Generation++;
            SettlePending = true;
        }

        internal void Bind(EntityBehaviour owner)
        {
            if (behaviour != null && behaviour != owner)
            {
                throw new InvalidOperationException("this TransformSync already belongs to another behaviour");
            }

            behaviour = owner;
        }

        internal void CaptureSpawn(in Vector3 localPosition, in Quaternion localRotation, in Vector3 localScale)
        {
            MarkSettled(localPosition, localRotation, localScale);
            SettlePending = true;
        }

        internal void MarkSettled(in Vector3 localPosition, in Quaternion localRotation, in Vector3 localScale)
        {
            sentPosition = sampledPosition = localPosition;
            sentRotation = sampledRotation = localRotation;
            sentScale = sampledScale = localScale;
            moving = false;
        }

        internal TransformSend Sample(in Vector3 localPosition, in Quaternion localRotation, in Vector3 localScale, out byte mask)
        {
            TransformSyncSettings settings = Settings;
            bool changed = (settings.SyncPosition && !Same(localPosition, sampledPosition))
                || (settings.SyncRotation && !Same(localRotation, sampledRotation))
                || (settings.SyncScale && !Same(localScale, sampledScale));
            sampledPosition = localPosition;
            sampledRotation = localRotation;
            sampledScale = localScale;
            mask = 0;
            if (changed)
            {
                moving = true;
                if (settings.SyncPosition && Distance(localPosition, sentPosition) > settings.PositionThreshold)
                {
                    mask |= SpawnTransform.PositionBit;
                    sentPosition = localPosition;
                }

                if (settings.SyncRotation && Angle(localRotation, sentRotation) > settings.RotationThreshold)
                {
                    mask |= SpawnTransform.RotationBit;
                    sentRotation = localRotation;
                }

                if (settings.SyncScale && Distance(localScale, sentScale) > settings.ScaleThreshold)
                {
                    mask |= SpawnTransform.ScaleBit;
                    sentScale = localScale;
                }

                return mask == 0 ? TransformSend.None : TransformSend.Update;
            }

            if (!moving)
            {
                return TransformSend.None;
            }

            MarkSettled(localPosition, localRotation, localScale);
            mask = SelectedMask;
            return TransformSend.Settle;
        }

        internal static float Distance(in Vector3 a, in Vector3 b)
        {
            float x = a.X - b.X;
            float y = a.Y - b.Y;
            float z = a.Z - b.Z;
            return (float)Math.Sqrt((x * x) + (y * y) + (z * z));
        }

        internal static float Angle(in Quaternion a, in Quaternion b)
        {
            float dot = Math.Min(Math.Abs((a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z) + (a.W * b.W)), 1f);
            return dot > SameRotationDot ? 0f : (float)Math.Acos(dot) * 2f * RadiansToDegrees;
        }

        private static bool Same(in Vector3 a, in Vector3 b) =>
            a.X.Equals(b.X) && a.Y.Equals(b.Y) && a.Z.Equals(b.Z);

        private static bool Same(in Quaternion a, in Quaternion b) =>
            a.X.Equals(b.X) && a.Y.Equals(b.Y) && a.Z.Equals(b.Z) && a.W.Equals(b.W);
    }
}
