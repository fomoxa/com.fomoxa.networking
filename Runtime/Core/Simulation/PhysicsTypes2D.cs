using System.Numerics;

namespace Fomoxa.Networking.Simulation
{
    public enum ShapeKind2D
    {
        Box,
        Circle,
        Capsule,
    }

    public readonly struct BodyShape2D
    {
        private BodyShape2D(ShapeKind2D kind, Vector2 halfExtents, float radius, float halfHeight)
        {
            Kind = kind;
            HalfExtents = halfExtents;
            Radius = radius;
            HalfHeight = halfHeight;
        }

        public ShapeKind2D Kind { get; }

        public Vector2 HalfExtents { get; }

        public float Radius { get; }

        public float HalfHeight { get; }

        public static BodyShape2D Box(Vector2 halfExtents) => new BodyShape2D(ShapeKind2D.Box, halfExtents, 0f, 0f);

        public static BodyShape2D Circle(float radius) => new BodyShape2D(ShapeKind2D.Circle, Vector2.Zero, radius, 0f);

        public static BodyShape2D Capsule(float radius, float halfHeight) => new BodyShape2D(ShapeKind2D.Capsule, Vector2.Zero, radius, halfHeight);
    }

    public readonly struct BodyDesc2D
    {
        public BodyDesc2D(BodyKind kind, BodyShape2D shape, Vector2 position, float rotation, float mass)
        {
            Kind = kind;
            Shape = shape;
            Position = position;
            Rotation = rotation;
            Mass = mass;
        }

        public BodyKind Kind { get; }

        public BodyShape2D Shape { get; }

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
