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

        [Network("Array<SceneFileCollider>")]
        [Codec("net")]
        public List<SceneFileCollider> Colliders { get; set; } = new List<SceneFileCollider>();

        [Network("Array<SceneFileCollider2D>")]
        [Codec("net")]
        public List<SceneFileCollider2D> Colliders2D { get; set; } = new List<SceneFileCollider2D>();

        [Network("Array<u32>")]
        [Codec("net")]
        public List<uint> LayerCollisions { get; set; } = new List<uint>();

        [Network("Array<u32>")]
        [Codec("net")]
        public List<uint> LayerCollisions2D { get; set; } = new List<uint>();

        [Network("Array<SceneFileEffector2D>")]
        [Codec("net")]
        public List<SceneFileEffector2D> Effectors2D { get; set; } = new List<SceneFileEffector2D>();
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

        [Network("SceneFileBody")]
        [Codec("net")]
        public SceneFileBody Body { get; set; } = new SceneFileBody();

        [Network("SceneFileBody2D")]
        [Codec("net")]
        public SceneFileBody2D Body2D { get; set; } = new SceneFileBody2D();

        [Network("Array<SceneFileChildBody>")]
        [Codec("net")]
        public List<SceneFileChildBody> ChildBodies { get; set; } = new List<SceneFileChildBody>();

        [Network("Array<SceneFileChildBody2D>")]
        [Codec("net")]
        public List<SceneFileChildBody2D> ChildBodies2D { get; set; } = new List<SceneFileChildBody2D>();

        [Network("Array<i32>")]
        [Codec("net")]
        public List<int> BehaviourBodies { get; set; } = new List<int>();

        [Network("Array<i32>")]
        [Codec("net")]
        public List<int> BehaviourBodies2D { get; set; } = new List<int>();
    }

    [Network]
    [Codec("net")]
    public class SceneFileChildBody
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
        public float RotationW { get; set; } = 1f;

        [Network("SceneFileBody")]
        [Codec("net")]
        public SceneFileBody Body { get; set; } = new SceneFileBody();
    }

    [Network]
    [Codec("net")]
    public class SceneFileChildBody2D
    {
        [Network("f32")]
        [Codec("net")]
        public float PositionX { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float PositionY { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float Rotation { get; set; }

        [Network("SceneFileBody2D")]
        [Codec("net")]
        public SceneFileBody2D Body { get; set; } = new SceneFileBody2D();
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

    [Network]
    [Codec("net")]
    public class SceneFileBody
    {
        [Network("u8")]
        [Codec("net")]
        public byte Kind { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float Mass { get; set; }

        [Network("Array<SceneFileCollider>")]
        [Codec("net")]
        public List<SceneFileCollider> Colliders { get; set; } = new List<SceneFileCollider>();

        [Network("u8")]
        [Codec("net")]
        public byte Locks { get; set; }

        [Network("bool")]
        [Codec("net")]
        public bool UseGravity { get; set; } = true;

        [Network("f32")]
        [Codec("net")]
        public float LinearDamping { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float AngularDamping { get; set; }

        [Network("bool")]
        [Codec("net")]
        public bool ContinuousCollision { get; set; }
    }

    [Network]
    [Codec("net")]
    public class SceneFileBody2D
    {
        [Network("u8")]
        [Codec("net")]
        public byte Kind { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float Mass { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float Rotation { get; set; }

        [Network("Array<SceneFileCollider2D>")]
        [Codec("net")]
        public List<SceneFileCollider2D> Colliders { get; set; } = new List<SceneFileCollider2D>();

        [Network("u8")]
        [Codec("net")]
        public byte Locks { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float GravityScale { get; set; } = 1f;

        [Network("f32")]
        [Codec("net")]
        public float LinearDamping { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float AngularDamping { get; set; }

        [Network("bool")]
        [Codec("net")]
        public bool ContinuousCollision { get; set; }
    }

    [Network]
    [Codec("net")]
    public class SceneFileCollider
    {
        [Network("u8")]
        [Codec("net")]
        public byte Kind { get; set; }

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
        public float HalfExtentsX { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float HalfExtentsY { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float HalfExtentsZ { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float Radius { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float HalfHeight { get; set; }

        [Network("Array<f32>")]
        [Codec("net")]
        public List<float> Points { get; set; } = new List<float>();

        [Network("Array<u32>")]
        [Codec("net")]
        public List<uint> Triangles { get; set; } = new List<uint>();

        [Network("SceneFileMaterial")]
        [Codec("net")]
        public SceneFileMaterial Material { get; set; } = new SceneFileMaterial();

        [Network("u8")]
        [Codec("net")]
        public byte Layer { get; set; }

        [Network("bool")]
        [Codec("net")]
        public bool IsTrigger { get; set; }
    }

    [Network]
    [Codec("net")]
    public class SceneFileCollider2D
    {
        [Network("u8")]
        [Codec("net")]
        public byte Kind { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float PositionX { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float PositionY { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float Rotation { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float HalfExtentsX { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float HalfExtentsY { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float Radius { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float HalfHeight { get; set; }

        [Network("Array<f32>")]
        [Codec("net")]
        public List<float> Points { get; set; } = new List<float>();

        [Network("SceneFileMaterial")]
        [Codec("net")]
        public SceneFileMaterial Material { get; set; } = new SceneFileMaterial();

        [Network("u8")]
        [Codec("net")]
        public byte Layer { get; set; }

        [Network("bool")]
        [Codec("net")]
        public bool IsTrigger { get; set; }

        [Network("u16")]
        [Codec("net")]
        public ushort Effector { get; set; }
    }

    [Network]
    [Codec("net")]
    public class SceneFileEffector2D
    {
        [Network("u8")]
        [Codec("net")]
        public byte Kind { get; set; }

        [Network("bool")]
        [Codec("net")]
        public bool UseColliderMask { get; set; }

        [Network("u32")]
        [Codec("net")]
        public uint ColliderMask { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float Rotation { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float OriginX { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float OriginY { get; set; }

        [Network("bool")]
        [Codec("net")]
        public bool OneWay { get; set; }

        [Network("bool")]
        [Codec("net")]
        public bool OneWayGrouping { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float RotationalOffset { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float SurfaceArc { get; set; }

        [Network("bool")]
        [Codec("net")]
        public bool SideFriction { get; set; }

        [Network("bool")]
        [Codec("net")]
        public bool SideBounce { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float SideArc { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float Speed { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float ForceScale { get; set; }

        [Network("bool")]
        [Codec("net")]
        public bool ContactForce { get; set; }

        [Network("bool")]
        [Codec("net")]
        public bool Friction { get; set; }

        [Network("bool")]
        [Codec("net")]
        public bool Bounce { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float ForceAngle { get; set; }

        [Network("bool")]
        [Codec("net")]
        public bool GlobalAngle { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float ForceMagnitude { get; set; }

        [Network("u8")]
        [Codec("net")]
        public byte ForceSource { get; set; }

        [Network("u8")]
        [Codec("net")]
        public byte ForceTarget { get; set; }

        [Network("u8")]
        [Codec("net")]
        public byte ForceMode { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float DistanceScale { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float LinearDamping { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float AngularDamping { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float Density { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float SurfaceLevel { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float FlowAngle { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float FlowMagnitude { get; set; }
    }

    [Network]
    [Codec("net")]
    public class SceneFileMaterial
    {
        [Network("f32")]
        [Codec("net")]
        public float Friction { get; set; }

        [Network("f32")]
        [Codec("net")]
        public float Restitution { get; set; }

        [Network("u8")]
        [Codec("net")]
        public byte FrictionCombine { get; set; }

        [Network("u8")]
        [Codec("net")]
        public byte RestitutionCombine { get; set; }
    }
}
