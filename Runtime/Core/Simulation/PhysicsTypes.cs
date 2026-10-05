using System;
using System.Numerics;

namespace Fomoxa.Networking.Simulation
{
    public enum BodyKind
    {
        Dynamic,
        Kinematic,
        Static,
    }

    public enum ShapeKind
    {
        Box,
        Sphere,
        Capsule,
    }

    public readonly struct BodyHandle : IEquatable<BodyHandle>
    {
        public BodyHandle(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public bool IsValid => Value != 0;

        public bool Equals(BodyHandle other) => Value == other.Value;

        public override bool Equals(object obj) => obj is BodyHandle other && Equals(other);

        public override int GetHashCode() => Value;
    }

    public readonly struct BodyShape
    {
        private BodyShape(ShapeKind kind, Vector3 halfExtents, float radius, float halfHeight)
        {
            Kind = kind;
            HalfExtents = halfExtents;
            Radius = radius;
            HalfHeight = halfHeight;
        }

        public ShapeKind Kind { get; }

        public Vector3 HalfExtents { get; }

        public float Radius { get; }

        public float HalfHeight { get; }

        public static BodyShape Box(Vector3 halfExtents) => new BodyShape(ShapeKind.Box, halfExtents, 0f, 0f);

        public static BodyShape Sphere(float radius) => new BodyShape(ShapeKind.Sphere, Vector3.Zero, radius, 0f);

        public static BodyShape Capsule(float radius, float halfHeight) => new BodyShape(ShapeKind.Capsule, Vector3.Zero, radius, halfHeight);
    }

    public readonly struct BodyDesc
    {
        public BodyDesc(BodyKind kind, BodyShape shape, Vector3 position, Quaternion rotation, float mass)
        {
            Kind = kind;
            Shape = shape;
            Position = position;
            Rotation = rotation;
            Mass = mass;
        }

        public BodyKind Kind { get; }

        public BodyShape Shape { get; }

        public Vector3 Position { get; }

        public Quaternion Rotation { get; }

        public float Mass { get; }
    }

    public struct BodyState
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public Vector3 AngularVelocity;
    }

    public readonly struct RayHit
    {
        public RayHit(BodyHandle body, Vector3 point, Vector3 normal, float distance)
        {
            Body = body;
            Point = point;
            Normal = normal;
            Distance = distance;
        }

        public BodyHandle Body { get; }

        public Vector3 Point { get; }

        public Vector3 Normal { get; }

        public float Distance { get; }
    }
}
