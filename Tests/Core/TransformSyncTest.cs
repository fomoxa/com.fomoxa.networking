using System;
using System.Collections.Generic;
using System.Numerics;
using Fomoxa.Networking.Objects;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class TransformSyncTest
    {
        private static readonly TransformSyncSettings Defaults = new TransformSyncSettings(true, true, false, 0.01f, 0.1f, 0.01f);

        [Test]
        public void AMoveBelowTheThresholdWaitsForTheSettle()
        {
            var sync = new TransformSync(Defaults);
            sync.CaptureSpawn(Vector3.Zero, Quaternion.Identity, Vector3.One);

            Assert.AreEqual(TransformSend.None, sync.Sample(new Vector3(0.005f, 0, 0), Quaternion.Identity, Vector3.One, out byte moving));
            Assert.AreEqual(0, moving);
            Assert.AreEqual(TransformSend.Settle, sync.Sample(new Vector3(0.005f, 0, 0), Quaternion.Identity, Vector3.One, out byte settled));
            Assert.AreEqual(SpawnTransform.PositionBit | SpawnTransform.RotationBit, settled);
            Assert.AreEqual(TransformSend.None, sync.Sample(new Vector3(0.005f, 0, 0), Quaternion.Identity, Vector3.One, out _));
        }

        [Test]
        public void AMoveAboveTheThresholdSendsOnlyTheChangedPart()
        {
            var sync = new TransformSync(Defaults);
            sync.CaptureSpawn(Vector3.Zero, Quaternion.Identity, Vector3.One);

            Assert.AreEqual(TransformSend.Update, sync.Sample(new Vector3(1, 0, 0), Quaternion.Identity, Vector3.One, out byte mask));

            Assert.AreEqual(SpawnTransform.PositionBit, mask);
        }

        [Test]
        public void TheRotationThresholdUsesTheAngleOfUnity()
        {
            Quaternion small = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.05f * MathF.PI / 180f);
            Quaternion large = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.2f * MathF.PI / 180f);
            var sync = new TransformSync(Defaults);
            sync.CaptureSpawn(Vector3.Zero, Quaternion.Identity, Vector3.One);

            Assert.AreEqual(TransformSend.None, sync.Sample(Vector3.Zero, small, Vector3.One, out _));
            Assert.AreEqual(TransformSend.Update, sync.Sample(Vector3.Zero, large, Vector3.One, out byte mask));
            Assert.AreEqual(SpawnTransform.RotationBit, mask);
            Assert.AreEqual(10f, TransformSync.Angle(Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 10f * MathF.PI / 180f)), 0.001f);
            Assert.AreEqual(0f, TransformSync.Angle(Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.01f * MathF.PI / 180f)));
        }

        [Test]
        public void NewSettingsApplyToTheNextSample()
        {
            var sync = new TransformSync(Defaults);
            sync.CaptureSpawn(Vector3.Zero, Quaternion.Identity, Vector3.One);
            sync.Settings = new TransformSyncSettings(false, true, false, 0.01f, 0.1f, 0.01f);

            Assert.AreEqual(TransformSend.None, sync.Sample(new Vector3(1, 0, 0), Quaternion.Identity, Vector3.One, out _));
            Assert.AreEqual(SpawnTransform.RotationBit, sync.SelectedMask);
        }

        [Test]
        public void TeleportBeforeTheServerSpawnDoesNothing()
        {
            var sync = new TransformSync(Defaults);

            sync.Teleport();

            Assert.AreEqual(0, sync.Generation);
            Assert.IsFalse(sync.SettlePending);
        }

        [Test]
        public void TheServerSendsASettleAfterSpawnThenUpdatesAndTheClientReceivesThem()
        {
            var world = new ClientEntitiesTest.World();
            var onClient = new Mover("client", world.ClientCalls);
            world.Backend.CreateBehaviour = () => onClient;
            var onServer = new Mover("server", world.ServerCalls) { Position = new Vector3(1, 0, 0) };
            world.Server.Spawn(world.Served(onServer), 0);
            world.Run(10);

            Assert.AreEqual(1, onClient.Resets);

            world.Server.SyncTransforms(1);
            world.Run(5);
            onServer.Position = new Vector3(2, 0, 0);
            world.Server.SyncTransforms(2);
            world.Run(5);
            world.Server.SyncTransforms(3);
            world.Run(5);

            Assert.AreEqual(3, onClient.Received.Count);
            AssertSample(onClient.Received[0], 1, SpawnTransform.PositionBit | SpawnTransform.RotationBit, new Vector3(1, 0, 0), true, 0);
            AssertSample(onClient.Received[1], 2, SpawnTransform.PositionBit, new Vector3(2, 0, 0), false, 0);
            AssertSample(onClient.Received[2], 3, SpawnTransform.PositionBit | SpawnTransform.RotationBit, new Vector3(2, 0, 0), true, 0);
        }

        [Test]
        public void TeleportOnTheServerSendsASettleOfTheNextGeneration()
        {
            var world = new ClientEntitiesTest.World();
            var onClient = new Mover("client", world.ClientCalls);
            world.Backend.CreateBehaviour = () => onClient;
            var onServer = new Mover("server", world.ServerCalls);
            world.Server.Spawn(world.Served(onServer), 0);
            world.Run(10);
            world.Server.SyncTransforms(1);
            world.Run(5);

            onServer.Position = new Vector3(50, 0, 0);
            onServer.Sync.Teleport();
            world.Server.SyncTransforms(2);
            world.Run(5);

            Assert.AreEqual(1, onServer.Sync.Generation);
            AssertSample(onClient.Received[onClient.Received.Count - 1], 2, SpawnTransform.PositionBit | SpawnTransform.RotationBit, new Vector3(50, 0, 0), true, 1);
        }

        private static void AssertSample(TransformSample sample, uint tick, int mask, Vector3 position, bool settle, byte generation)
        {
            Assert.AreEqual(tick, sample.Tick);
            Assert.AreEqual(mask, sample.Mask);
            Assert.AreEqual(position, sample.LocalPosition);
            Assert.AreEqual(settle, sample.Settle);
            Assert.AreEqual(generation, sample.Generation);
        }

        private sealed class Mover : ClientEntitiesTest.RecordingBehaviour, ITransformSource, ITransformReceiver
        {
            public Mover(string name, List<string> calls)
                : base(name, calls)
            {
                Sync = new TransformSync(Defaults);
                SyncTransform(Sync, this, this);
            }

            public TransformSync Sync { get; }

            public Vector3 Position { get; set; }

            public List<TransformSample> Received { get; } = new List<TransformSample>();

            public int Resets { get; private set; }

            public void ReadLocal(out Vector3 localPosition, out Quaternion localRotation, out Vector3 localScale)
            {
                localPosition = Position;
                localRotation = Quaternion.Identity;
                localScale = Vector3.One;
            }

            public void ResetReceive() => Resets++;

            public void Receive(in TransformSample sample) => Received.Add(sample);
        }
    }
}
