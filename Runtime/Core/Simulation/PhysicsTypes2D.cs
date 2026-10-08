using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;

namespace Fomoxa.Networking.Simulation
{
    public enum ShapeKind2D
    {
        Box,
        Circle,
        Capsule,
        ConvexPolygon,
        Polyline,
    }

    public readonly struct BodyShape2D
    {
        private readonly IReadOnlyList<Vector2> points;

        private BodyShape2D(ShapeKind2D kind, Vector2 halfExtents, float radius, float halfHeight, IReadOnlyList<Vector2> points)
        {
            Kind = kind;
            HalfExtents = halfExtents;
            Radius = radius;
            HalfHeight = halfHeight;
            this.points = points;
        }

        public ShapeKind2D Kind { get; }

        public Vector2 HalfExtents { get; }

        public float Radius { get; }

        public float HalfHeight { get; }

        public IReadOnlyList<Vector2> Points => points ?? Array.Empty<Vector2>();

        internal bool StaticOnly => Kind == ShapeKind2D.Polyline;

        public static BodyShape2D Box(Vector2 halfExtents) => new BodyShape2D(ShapeKind2D.Box, halfExtents, 0f, 0f, null);

        public static BodyShape2D Circle(float radius) => new BodyShape2D(ShapeKind2D.Circle, Vector2.Zero, radius, 0f, null);

        public static BodyShape2D Capsule(float radius, float halfHeight) => new BodyShape2D(ShapeKind2D.Capsule, Vector2.Zero, radius, halfHeight, null);

        public static BodyShape2D ConvexPolygon(IReadOnlyList<Vector2> points)
        {
            ReadOnlyCollection<Vector2> copied = ShapeData.Copy(points, nameof(points));
            if (copied.Count < 3)
            {
                throw new ArgumentException("a convex polygon needs at least 3 points", nameof(points));
            }

            return new BodyShape2D(ShapeKind2D.ConvexPolygon, Vector2.Zero, 0f, 0f, copied);
        }

        public static BodyShape2D Polyline(IReadOnlyList<Vector2> points)
        {
            ReadOnlyCollection<Vector2> copied = ShapeData.Copy(points, nameof(points));
            if (copied.Count < 2)
            {
                throw new ArgumentException("a polyline needs at least 2 points", nameof(points));
            }

            return new BodyShape2D(ShapeKind2D.Polyline, Vector2.Zero, 0f, 0f, copied);
        }
    }

    public readonly struct ColliderDesc2D
    {
        public ColliderDesc2D(BodyShape2D shape, Vector2 position, float rotation, ColliderMaterial material, int layer, bool isTrigger)
        {
            ShapeData.CheckLayer(layer);
            Shape = shape;
            Position = position;
            Rotation = rotation;
            Material = material;
            Layer = layer;
            IsTrigger = isTrigger;
        }

        public BodyShape2D Shape { get; }

        public Vector2 Position { get; }

        public float Rotation { get; }

        public ColliderMaterial Material { get; }

        public int Layer { get; }

        public bool IsTrigger { get; }
    }

    [Flags]
    public enum BodyLocks2D
    {
        None = 0,
        PositionX = 1,
        PositionY = 2,
        Rotation = 4,
    }

    public readonly struct BodyMotion2D
    {
        public BodyMotion2D(BodyLocks2D locks, float gravityScale, float linearDamping, float angularDamping)
        {
            BodyMotionChecks.CheckGravityScale(gravityScale, nameof(gravityScale));
            BodyMotionChecks.CheckDamping(linearDamping, nameof(linearDamping));
            BodyMotionChecks.CheckDamping(angularDamping, nameof(angularDamping));
            Locks = locks;
            GravityScale = gravityScale;
            LinearDamping = linearDamping;
            AngularDamping = angularDamping;
        }

        public static BodyMotion2D Default => new BodyMotion2D(BodyLocks2D.None, 1f, 0f, 0f);

        public BodyLocks2D Locks { get; }

        public float GravityScale { get; }

        public float LinearDamping { get; }

        public float AngularDamping { get; }
    }

    public readonly struct BodyDesc2D
    {
        private readonly IReadOnlyList<ColliderDesc2D> colliders;
        private readonly BodyMotion2D motion;
        private readonly bool hasMotion;

        public BodyDesc2D(BodyKind kind, BodyShape2D shape, Vector2 position, float rotation, float mass)
            : this(kind, new[] { new ColliderDesc2D(shape, Vector2.Zero, 0f, ColliderMaterial.Default, 0, false) }, position, rotation, mass)
        {
        }

        public BodyDesc2D(BodyKind kind, IReadOnlyList<ColliderDesc2D> colliders, Vector2 position, float rotation, float mass)
            : this(kind, colliders, position, rotation, mass, BodyMotion2D.Default)
        {
        }

        public BodyDesc2D(BodyKind kind, IReadOnlyList<ColliderDesc2D> colliders, Vector2 position, float rotation, float mass, in BodyMotion2D motion)
        {
            ReadOnlyCollection<ColliderDesc2D> copied = ShapeData.Copy(colliders, nameof(colliders));
            if (copied.Count == 0)
            {
                throw new ArgumentException("a body needs at least one collider", nameof(colliders));
            }

            foreach (ColliderDesc2D collider in copied)
            {
                if (collider.Shape.StaticOnly && kind != BodyKind.Static)
                {
                    throw new ArgumentException($"a {collider.Shape.Kind} collider needs a static body", nameof(colliders));
                }
            }

            Kind = kind;
            this.colliders = copied;
            Position = position;
            Rotation = rotation;
            Mass = mass;
            this.motion = motion;
            hasMotion = true;
        }

        public BodyKind Kind { get; }

        public IReadOnlyList<ColliderDesc2D> Colliders => colliders ?? Array.Empty<ColliderDesc2D>();

        public BodyMotion2D Motion => hasMotion ? motion : BodyMotion2D.Default;

        public Vector2 Position { get; }

        public float Rotation { get; }

        public float Mass { get; }
    }

    public struct BodyState2D
    {
        public Vector2 Position;
        public float Rotation;
        public Vector2 Velocity;
        public float AngularVelocity;
    }

    public readonly struct RayHit2D
    {
        public RayHit2D(BodyHandle body, Vector2 point, Vector2 normal, float distance)
        {
            Body = body;
            Point = point;
            Normal = normal;
            Distance = distance;
        }

        public BodyHandle Body { get; }

        public Vector2 Point { get; }

        public Vector2 Normal { get; }

        public float Distance { get; }
    }
}
