using System;
using System.Collections.Generic;
using Fomoxa.Networking;
using Fomoxa.Networking.Objects;
using UnityEngine;

namespace Fomoxa.Unity
{
    public sealed class NetworkTransform : NetworkBehaviour, ITransformSource, ITransformReceiver
    {
        [SerializeField] private bool syncPosition = true;
        [SerializeField] private bool syncRotation = true;
        [SerializeField] private bool syncScale;
        [SerializeField] private float positionThreshold = 0.01f;
        [SerializeField] private float rotationThreshold = 0.1f;
        [SerializeField] private float scaleThreshold = 0.01f;
        [SerializeField] private int interpolationTicks = 2;
        [SerializeField] private Transform visual;
        [SerializeField] private float smoothingTime = 0.1f;
        [SerializeField] private bool drawCorrections;
        [SerializeField, Min(MinimumCorrectionSeconds)] private float correctionSeconds = 2f;

        internal const int MaxDrawnCorrections = 64;
        private const float MinimumCorrectionSeconds = 0.1f;
        private const float CorrectionDotRadius = 0.05f;

        private readonly List<Snapshot> snapshots = new List<Snapshot>();
        private readonly Queue<DrawnCorrection> drawnCorrections = new Queue<DrawnCorrection>();
        private TransformSync sync;
        private bool received;
        private byte receivedMask;
        private byte receivedGeneration;
        private double renderTick;
        private int tickRate;
        private Vector3 visualRestPosition;
        private Quaternion visualRestRotation;
        private Vector3 correctedPosition;
        private Quaternion correctedRotation;
        private Vector3 smoothingOffset;
        private Quaternion smoothingTurn = Quaternion.identity;
        private float smoothingLeft;
        private Vector3 positionBeforeCorrection;
        private Quaternion rotationBeforeCorrection;

        public event Action<TransformCorrection> OnCorrected;

        public bool SyncPosition => syncPosition;

        public bool SyncRotation => syncRotation;

        public bool SyncScale => syncScale;

        public int InterpolationTicks => interpolationTicks;

        public Transform Visual
        {
            get => visual;
            set => visual = value;
        }

        public float SmoothingTime
        {
            get => smoothingTime;
            set => smoothingTime = Mathf.Max(0f, value);
        }

        public TransformCorrection? LastCorrection { get; private set; }

        public bool DrawCorrections
        {
            get => drawCorrections;
            set => drawCorrections = value;
        }

        public float CorrectionSeconds
        {
            get => Mathf.Max(MinimumCorrectionSeconds, correctionSeconds);
            set => correctionSeconds = Mathf.Max(MinimumCorrectionSeconds, value);
        }

        internal int DrawnCorrectionCount => drawnCorrections.Count;

        internal bool SettlePending => sync != null && sync.SettlePending;

        internal byte Generation => sync != null ? sync.Generation : (byte)0;

        internal uint LastTick { get; private set; }

        internal int SnapshotCount => snapshots.Count;

        public void Teleport() => sync?.Teleport();

        internal override void OnAttached()
        {
            if (sync != null)
            {
                return;
            }

            sync = new TransformSync(CurrentSettings());
            TransformTarget target = NetworkObject != null && (NetworkObject.gameObject == gameObject || TryGetComponent(out Rigidbody _) || (TryGetComponent(out Rigidbody2D body2D) && body2D.simulated)) ? TransformTarget.BodyRoot : TransformTarget.Source;
            Core.SetTransform(sync, this, this, target);
        }

        void ITransformSource.ReadLocal(out System.Numerics.Vector3 localPosition, out System.Numerics.Quaternion localRotation, out System.Numerics.Vector3 localScale)
        {
            Transform target = transform;
            localPosition = target.localPosition.ToNumerics();
            localRotation = target.localRotation.ToNumerics();
            localScale = target.localScale.ToNumerics();
        }

        void ITransformReceiver.ResetReceive() => ResetReceive();

        void ITransformReceiver.Receive(in TransformSample sample) =>
            Receive(
                sample.Tick,
                sample.Mask,
                sample.LocalPosition.ToUnity(),
                sample.LocalRotation.ToUnity(),
                sample.LocalScale.ToUnity(),
                sample.Settle,
                sample.Generation,
                NetworkObject.Client.TickRate);

        internal void ResetReceive()
        {
            received = false;
            receivedMask = 0;
            renderTick = 0;
            LastTick = 0;
            snapshots.Clear();
        }

        internal void Receive(uint tick, byte mask, Vector3 position, Quaternion rotation, Vector3 scale, bool settle, byte generation, int rate)
        {
            bool newGeneration = received && generation != receivedGeneration;
            if (received && tick <= LastTick)
            {
                if (settle && !newGeneration)
                {
                    InsertBarrier(tick, mask, position, rotation, scale);
                }

                return;
            }

            tickRate = rate;
            Snapshot previous = snapshots.Count > 0 ? snapshots[snapshots.Count - 1] : Snapshot.Of(transform, LastTick);
            var next = new Snapshot(
                tick,
                (mask & SpawnTransform.PositionBit) != 0 ? position : previous.Position,
                (mask & SpawnTransform.RotationBit) != 0 ? rotation : previous.Rotation,
                (mask & SpawnTransform.ScaleBit) != 0 ? scale : previous.Scale);
            receivedMask |= mask;
            LastTick = tick;
            receivedGeneration = generation;
            if (settle || !received || newGeneration)
            {
                received = true;
                snapshots.Clear();
                snapshots.Add(next);
                renderTick = tick;
                Apply(next);
                return;
            }

            snapshots.Add(next);
        }

        private void InsertBarrier(uint tick, byte mask, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            int newer = 0;
            while (newer < snapshots.Count && snapshots[newer].Tick <= tick)
            {
                newer++;
            }

            Snapshot previous = newer > 0 ? snapshots[newer - 1] : Snapshot.Of(transform, tick);
            var barrier = new Snapshot(
                tick,
                (mask & SpawnTransform.PositionBit) != 0 ? position : previous.Position,
                (mask & SpawnTransform.RotationBit) != 0 ? rotation : previous.Rotation,
                (mask & SpawnTransform.ScaleBit) != 0 ? scale : previous.Scale);
            receivedMask |= mask;
            snapshots.RemoveRange(0, newer);
            snapshots.Insert(0, barrier);
        }

        internal void BeginCorrection()
        {
            Transform shown = visual != null ? visual : transform;
            positionBeforeCorrection = shown.position;
            rotationBeforeCorrection = shown.rotation;
            if (visual == null)
            {
                return;
            }

            if (smoothingLeft <= 0f)
            {
                visualRestPosition = visual.localPosition;
                visualRestRotation = visual.localRotation;
            }

            correctedPosition = visual.position;
            correctedRotation = visual.rotation;
        }

        internal void EndCorrection(uint tick)
        {
            if (visual == null)
            {
                Corrected(tick, transform);
                return;
            }

            visual.localPosition = visualRestPosition;
            visual.localRotation = visualRestRotation;
            Corrected(tick, visual);
            if (smoothingTime <= 0f)
            {
                smoothingLeft = 0f;
                return;
            }

            visual.position = correctedPosition;
            visual.rotation = correctedRotation;
            smoothingOffset = visual.localPosition - visualRestPosition;
            smoothingTurn = Quaternion.Inverse(visualRestRotation) * visual.localRotation;
            smoothingLeft = smoothingTime;
        }

        private void Corrected(uint tick, Transform shown)
        {
            var correction = new TransformCorrection(tick, positionBeforeCorrection, shown.position, rotationBeforeCorrection, shown.rotation);
            LastCorrection = correction;
            if (drawCorrections && Application.isEditor)
            {
                RecordDrawnCorrection(correction, Time.realtimeSinceStartup);
            }

            try
            {
                OnCorrected?.Invoke(correction);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        internal void RecordDrawnCorrection(TransformCorrection correction, float now)
        {
            if (drawnCorrections.Count >= MaxDrawnCorrections)
            {
                drawnCorrections.Dequeue();
            }

            drawnCorrections.Enqueue(new DrawnCorrection(correction, now));
        }

        internal void ForgetExpiredCorrections(float now)
        {
            while (drawnCorrections.Count > 0 && now - drawnCorrections.Peek().RecordedAt > CorrectionSeconds)
            {
                drawnCorrections.Dequeue();
            }
        }

        private void OnDrawGizmos()
        {
            if (!drawCorrections)
            {
                drawnCorrections.Clear();
                return;
            }

            ForgetExpiredCorrections(Time.realtimeSinceStartup);
            foreach (DrawnCorrection drawn in drawnCorrections)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(drawn.Correction.PositionBefore, CorrectionDotRadius);
                Gizmos.color = Color.green;
                Gizmos.DrawSphere(drawn.Correction.PositionAfter, CorrectionDotRadius);
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(drawn.Correction.PositionBefore, drawn.Correction.PositionAfter);
            }
        }

        internal void SmoothVisual(float seconds)
        {
            if (visual == null || smoothingLeft <= 0f)
            {
                return;
            }

            smoothingLeft = Mathf.Max(0f, smoothingLeft - seconds);
            float remaining = smoothingTime > 0f ? smoothingLeft / smoothingTime : 0f;
            visual.localPosition = visualRestPosition + (smoothingOffset * remaining);
            visual.localRotation = visualRestRotation * Quaternion.SlerpUnclamped(Quaternion.identity, smoothingTurn, remaining);
        }

        internal void Advance(double seconds)
        {
            if (snapshots.Count == 0)
            {
                return;
            }

            double newest = snapshots[snapshots.Count - 1].Tick;
            renderTick = Math.Min(newest, Math.Max(renderTick + (seconds * tickRate), newest - interpolationTicks));
            while (snapshots.Count > 1 && snapshots[1].Tick <= renderTick)
            {
                snapshots.RemoveAt(0);
            }

            Snapshot from = snapshots[0];
            if (snapshots.Count == 1 || renderTick <= from.Tick)
            {
                Apply(from);
                return;
            }

            Snapshot to = snapshots[1];
            float fraction = (float)((renderTick - from.Tick) / (to.Tick - from.Tick));
            Apply(new Snapshot(
                0,
                Vector3.LerpUnclamped(from.Position, to.Position, fraction),
                Quaternion.SlerpUnclamped(from.Rotation, to.Rotation, fraction),
                Vector3.LerpUnclamped(from.Scale, to.Scale, fraction)));
        }

        private void OnValidate()
        {
            if (sync != null)
            {
                sync.Settings = CurrentSettings();
            }
        }

        private TransformSyncSettings CurrentSettings() =>
            new TransformSyncSettings(syncPosition, syncRotation, syncScale, positionThreshold, rotationThreshold, scaleThreshold);

        private void Update()
        {
            NetworkObject networkObject = NetworkObject;
            if (networkObject != null && networkObject.Client != null && networkObject.Server == null)
            {
                Advance(Time.unscaledDeltaTime);
                SmoothVisual(Time.unscaledDeltaTime);
            }
        }

        private void Apply(Snapshot snapshot)
        {
            Transform target = transform;
            if ((receivedMask & SpawnTransform.PositionBit) != 0)
            {
                target.localPosition = snapshot.Position;
            }

            if ((receivedMask & SpawnTransform.RotationBit) != 0)
            {
                target.localRotation = snapshot.Rotation;
            }

            if ((receivedMask & SpawnTransform.ScaleBit) != 0)
            {
                target.localScale = snapshot.Scale;
            }
        }

        private readonly struct DrawnCorrection
        {
            public DrawnCorrection(TransformCorrection correction, float recordedAt)
            {
                Correction = correction;
                RecordedAt = recordedAt;
            }

            public TransformCorrection Correction { get; }

            public float RecordedAt { get; }
        }

        private readonly struct Snapshot
        {
            public Snapshot(uint tick, Vector3 position, Quaternion rotation, Vector3 scale)
            {
                Tick = tick;
                Position = position;
                Rotation = rotation;
                Scale = scale;
            }

            public uint Tick { get; }

            public Vector3 Position { get; }

            public Quaternion Rotation { get; }

            public Vector3 Scale { get; }

            public static Snapshot Of(Transform target, uint tick) =>
                new Snapshot(tick, target.localPosition, target.localRotation, target.localScale);
        }
    }
}
