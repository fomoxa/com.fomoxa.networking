using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        ConvexHull,
        TriangleMesh,
    }

    public enum CombineRule
    {
        Average,
        Minimum,
        Multiply,
        Maximum,
        Mean,
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

    public readonly struct ColliderMaterial
    {
        public ColliderMaterial(float friction, float restitution, CombineRule frictionCombine, CombineRule restitutionCombine)
        {
            if (friction < 0f || float.IsNaN(friction))
            {
                throw new ArgumentException("friction must not be negative", nameof(friction));
            }

            if (restitution < 0f || float.IsNaN(restitution))
            {
                throw new ArgumentException("restitution must not be negative", nameof(restitution));
            }

            Friction = friction;
            Restitution = restitution;
            FrictionCombine = frictionCombine;
            RestitutionCombine = restitutionCombine;
        }

        public static ColliderMaterial Default => new ColliderMaterial(0.6f, 0f, CombineRule.Average, CombineRule.Average);

        public float Friction { get; }

        public float Restitution { get; }

        public CombineRule FrictionCombine { get; }

        public CombineRule RestitutionCombine { get; }
    }

    public readonly struct BodyShape
    {
        private readonly IReadOnlyList<Vector3> points;
        private readonly IReadOnlyList<int> triangles;

        private BodyShape(ShapeKind kind, Vector3 halfExtents, float radius, float halfHeight, IReadOnlyList<Vector3> points, IReadOnlyList<int> triangles)
        {
            Kind = kind;
            HalfExtents = halfExtents;
            Radius = radius;
            HalfHeight = halfHeight;
            this.points = points;
            this.triangles = triangles;
        }

        public ShapeKind Kind { get; }

        public Vector3 HalfExtents { get; }

        public float Radius { get; }

        public float HalfHeight { get; }

        public IReadOnlyList<Vector3> Points => points ?? Array.Empty<Vector3>();

        public IReadOnlyList<int> Triangles => triangles ?? Array.Empty<int>();

        internal bool StaticOnly => Kind == ShapeKind.TriangleMesh;

        public static BodyShape Box(Vector3 halfExtents) => new BodyShape(ShapeKind.Box, halfExtents, 0f, 0f, null, null);

        public static BodyShape Sphere(float radius) => new BodyShape(ShapeKind.Sphere, Vector3.Zero, radius, 0f, null, null);

        public static BodyShape Capsule(float radius, float halfHeight) => new BodyShape(ShapeKind.Capsule, Vector3.Zero, radius, halfHeight, null, null);

        public static BodyShape ConvexHull(IReadOnlyList<Vector3> points)
        {
            ReadOnlyCollection<Vector3> copied = ShapeData.Copy(points, nameof(points));
            if (copied.Count < 4)
            {
                throw new ArgumentException("a convex hull needs at least 4 points", nameof(points));
            }

            return new BodyShape(ShapeKind.ConvexHull, Vector3.Zero, 0f, 0f, copied, null);
        }

        public static BodyShape TriangleMesh(IReadOnlyList<Vector3> vertices, IReadOnlyList<int> triangles)
        {
            ReadOnlyCollection<Vector3> copiedVertices = ShapeData.Copy(vertices, nameof(vertices));
            ReadOnlyCollection<int> copiedTriangles = ShapeData.Copy(triangles, nameof(triangles));
            if (copiedVertices.Count < 3)
            {
                throw new ArgumentException("a triangle mesh needs at least 3 vertices", nameof(vertices));
            }

            if (copiedTriangles.Count == 0 || copiedTriangles.Count % 3 != 0)
            {
                throw new ArgumentException("a triangle mesh needs a positive multiple of 3 indices", nameof(triangles));
            }

            foreach (int index in copiedTriangles)
            {
                if (index < 0 || index >= copiedVertices.Count)
                {
                    throw new ArgumentException($"triangle index {index} is outside the {copiedVertices.Count} vertices", nameof(triangles));
                }
            }

            return new BodyShape(ShapeKind.TriangleMesh, Vector3.Zero, 0f, 0f, copiedVertices, copiedTriangles);
        }
    }

    public readonly struct ColliderDesc
    {
        public ColliderDesc(BodyShape shape, Vector3 position, Quaternion rotation, ColliderMaterial material, int layer, bool isTrigger)
        {
            ShapeData.CheckLayer(layer);
            Shape = shape;
            Position = position;
            Rotation = rotation;
            Material = material;
            Layer = layer;
            IsTrigger = isTrigger;
        }

        public BodyShape Shape { get; }

        public Vector3 Position { get; }

        public Quaternion Rotation { get; }

        public ColliderMaterial Material { get; }

        public int Layer { get; }

        public bool IsTrigger { get; }
    }

    public readonly struct BodyDesc
    {
        private readonly IReadOnlyList<ColliderDesc> colliders;

        public BodyDesc(BodyKind kind, BodyShape shape, Vector3 position, Quaternion rotation, float mass)
            : this(kind, new[] { new ColliderDesc(shape, Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 0, false) }, position, rotation, mass)
        {
        }

        public BodyDesc(BodyKind kind, IReadOnlyList<ColliderDesc> colliders, Vector3 position, Quaternion rotation, float mass)
        {
            ReadOnlyCollection<ColliderDesc> copied = ShapeData.Copy(colliders, nameof(colliders));
            if (copied.Count == 0)
            {
                throw new ArgumentException("a body needs at least one collider", nameof(colliders));
            }

            foreach (ColliderDesc collider in copied)
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
        }

        public BodyKind Kind { get; }

        public IReadOnlyList<ColliderDesc> Colliders => colliders ?? Array.Empty<ColliderDesc>();

        public Vector3 Position { get; }

        public Quaternion Rotation { get; }

        public float Mass { get; }
    }

    internal static class ShapeData
    {
        public static ReadOnlyCollection<T> Copy<T>(IReadOnlyList<T> source, string name)
        {
            if (source == null)
            {
                throw new ArgumentNullException(name);
            }

            var copy = new T[source.Count];
            for (int index = 0; index < copy.Length; index++)
            {
                copy[index] = source[index];
            }

            return new ReadOnlyCollection<T>(copy);
        }

        public static void CheckLayer(int layer)
        {
            if (layer < 0 || layer > 31)
            {
                throw new ArgumentOutOfRangeException(nameof(layer), layer, "a layer is between 0 and 31");
            }
        }
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
