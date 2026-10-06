using BundleFixture;
using Fomoxa.Networking.Messaging;
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
        public void TheRegistrySchemaPublishesTheSceneFileFingerprint()
        {
            FomoxaRegistry registry = TestObjects.Registry();

            Assert.AreSame(SceneFileNetAdapter.Instance, registry.Codec<SceneFile>());
            Assert.AreEqual(Handshake.SceneFileNetFingerprint, registry.Schema.Message(Handshake.SceneFileNetMessageId).Fingerprint);
        }
    }
}
