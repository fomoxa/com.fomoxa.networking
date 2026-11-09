using System;
using System.Buffers.Binary;
using System.IO;
using BundleFixture;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class SceneFileTest
    {
        [Test]
        public void ASceneFileSurvivesEncodingAndDecoding()
        {
            var file = new SceneFile { SceneId = 0xA1B2C3D4 };
            file.Objects.Add(new SceneFileObject
            {
                SceneObjectId = 0x0102030405060708,
                Fingerprint = 0xCAFEF00D,
                BehaviourTypes = { "Game.Door", "Game.Health" },
                Pose = new SceneFilePose
                {
                    PositionX = 1,
                    PositionY = 2,
                    PositionZ = 3,
                    RotationX = 0,
                    RotationY = 0.7071068f,
                    RotationZ = 0,
                    RotationW = 0.7071068f,
                    ScaleX = 1,
                    ScaleY = 2,
                    ScaleZ = 1,
                },
            });
            file.Objects.Add(new SceneFileObject { SceneObjectId = 9, Fingerprint = 7 });

            var decoded = new SceneFile();
            SceneFileNetAdapter.Instance.Decode(SceneFileNetAdapter.Instance.Encode(file), ref decoded);

            Assert.AreEqual(file.SceneId, decoded.SceneId);
            Assert.AreEqual(2, decoded.Objects.Count);
            SceneFileObject door = decoded.Objects[0];
            Assert.AreEqual(0x0102030405060708UL, door.SceneObjectId);
            Assert.AreEqual(0xCAFEF00DU, door.Fingerprint);
            CollectionAssert.AreEqual(new[] { "Game.Door", "Game.Health" }, door.BehaviourTypes);
            Assert.AreEqual(3f, door.Pose.PositionZ);
            Assert.AreEqual(0.7071068f, door.Pose.RotationY);
            Assert.AreEqual(0.7071068f, door.Pose.RotationW);
            Assert.AreEqual(2f, door.Pose.ScaleY);
            Assert.AreEqual(9UL, decoded.Objects[1].SceneObjectId);
            Assert.IsEmpty(decoded.Objects[1].BehaviourTypes);
            Assert.AreEqual(0f, decoded.Objects[1].Pose.RotationW);
        }

        [Test]
        public void EffectorsChildBodiesAndBehaviourBodiesSurviveEncodingAndDecoding()
        {
            var file = new SceneFile { SceneId = 5 };
            file.Effectors2D.Add(new SceneFileEffector2D { Kind = 1, OneWay = true, SurfaceArc = 3.1415927f, RotationalOffset = 0.5f, OriginX = 2f });
            file.Colliders2D.Add(new SceneFileCollider2D { Kind = 1, HalfExtentsX = 2f, HalfExtentsY = 0.25f, Effector = 1 });
            var ragdoll = new SceneFileObject { SceneObjectId = 3, Fingerprint = 4, BehaviourTypes = { "Game.Limb" }, BehaviourBodies = { 0 }, BehaviourBodies2D = { -1 } };
            ragdoll.ChildBodies.Add(new SceneFileChildBody { PositionY = 1.5f, RotationZ = 0.25f, RotationW = 0.9682458f, Body = new SceneFileBody { Kind = 1, Mass = 2f } });
            ragdoll.ChildBodies2D.Add(new SceneFileChildBody2D { PositionX = -1f, Rotation = 0.75f, Body = new SceneFileBody2D { Kind = 2 } });
            file.Objects.Add(ragdoll);

            var decoded = new SceneFile();
            SceneFileNetAdapter.Instance.Decode(SceneFileNetAdapter.Instance.Encode(file), ref decoded);

            SceneFileEffector2D platform = decoded.Effectors2D[0];
            Assert.AreEqual(((byte)1, true, 3.1415927f, 0.5f, 2f), (platform.Kind, platform.OneWay, platform.SurfaceArc, platform.RotationalOffset, platform.OriginX));
            Assert.AreEqual(1, decoded.Colliders2D[0].Effector);
            SceneFileObject read = decoded.Objects[0];
            CollectionAssert.AreEqual(new[] { 0 }, read.BehaviourBodies);
            CollectionAssert.AreEqual(new[] { -1 }, read.BehaviourBodies2D);
            Assert.AreEqual((1.5f, 0.25f, 0.9682458f, 2f), (read.ChildBodies[0].PositionY, read.ChildBodies[0].RotationZ, read.ChildBodies[0].RotationW, read.ChildBodies[0].Body.Mass));
            Assert.AreEqual((-1f, 0.75f, (byte)2), (read.ChildBodies2D[0].PositionX, read.ChildBodies2D[0].Rotation, read.ChildBodies2D[0].Body.Kind));
        }

        [Test]
        public void TheFileFormatPutsTheModelFingerprintBeforeTheBody()
        {
            FomoxaRegistry registry = TestObjects.Registry();
            var file = new SceneFile { SceneId = 42 };
            file.Objects.Add(new SceneFileObject { SceneObjectId = 7, Fingerprint = 9 });

            byte[] bytes = SceneFileFormat.Write(registry, file);
            SceneFile read = SceneFileFormat.Read(registry, bytes);

            Assert.AreEqual(Handshake.SceneFileNetFingerprint, BinaryPrimitives.ReadUInt64LittleEndian(bytes));
            CollectionAssert.AreEqual(SceneFileNetAdapter.Instance.Encode(file).ToArray(), bytes.AsSpan(sizeof(ulong)).ToArray());
            Assert.AreEqual(42U, read.SceneId);
            Assert.AreEqual(7UL, read.Objects[0].SceneObjectId);
        }

        [Test]
        public void AFileForAnotherFingerprintOrWithoutAHeaderIsRefused()
        {
            FomoxaRegistry registry = TestObjects.Registry();
            byte[] bytes = SceneFileFormat.Write(registry, new SceneFile { SceneId = 42 });
            BinaryPrimitives.WriteUInt64LittleEndian(bytes, Handshake.SceneFileNetFingerprint + 1);

            InvalidDataException stale = Assert.Throws<InvalidDataException>(() => SceneFileFormat.Read(registry, bytes));
            StringAssert.Contains("export the scene again", stale.Message);
            Assert.Throws<InvalidDataException>(() => SceneFileFormat.Read(registry, new byte[4]));
        }

        [Test]
        public void TheFileFormatNeedsTheSceneFileCodecAndSchema()
        {
            Assert.Throws<InvalidOperationException>(() => SceneFileFormat.Write(new FomoxaRegistry(), new SceneFile()));
        }

        [Test]
        public void TheRegistrySchemaPublishesTheSceneFileFingerprint()
        {
            FomoxaRegistry registry = TestObjects.Registry();

            Assert.AreSame(SceneFileNetAdapter.Instance, registry.Codec<SceneFile>());
            Assert.AreEqual(Handshake.SceneFileNetFingerprint, registry.Schema.Message(Handshake.SceneFileNetMessageId).Fingerprint);
        }
    }
}
