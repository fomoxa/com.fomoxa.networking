using System.Collections.Generic;
using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    public class SceneFile
    {
        [Network("u32")]
        [Codec("net")]
        public uint SceneId { get; set; }

        [Network("Array<SceneFileObject>")]
        [Codec("net")]
        public List<SceneFileObject> Objects { get; set; } = new List<SceneFileObject>();
    }

    [Network]
    [Codec("net")]
    public class SceneFileObject
    {
        [Network("u64")]
        [Codec("net")]
        public ulong SceneObjectId { get; set; }

        [Network("u32")]
        [Codec("net")]
        public uint Fingerprint { get; set; }

        [Network("Array<string>")]
        [Codec("net")]
        public List<string> BehaviourTypes { get; set; } = new List<string>();

        [Network("SceneFilePose")]
        [Codec("net")]
        public SceneFilePose Pose { get; set; } = new SceneFilePose();
    }

    [Network]
    [Codec("net")]
    public class SceneFilePose
    {
        [Network("f32")]
        [Codec("net")]
        public float PositionX { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float PositionY { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float PositionZ { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float RotationX { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float RotationY { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float RotationZ { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float RotationW { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float ScaleX { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float ScaleY { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float ScaleZ { get; set; }
    }
}
