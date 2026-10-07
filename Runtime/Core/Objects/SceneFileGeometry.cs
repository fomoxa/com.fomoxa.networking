using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Objects
{
    public static class SceneFileGeometry
    {
        public static SceneFileCollider ToFile(in ColliderDesc collider)
        {
            BodyShape shape = collider.Shape;
            var file = new SceneFileCollider
            {
                Kind = (byte)shape.Kind,
                PositionX = collider.Position.X,
                PositionY = collider.Position.Y,
                PositionZ = collider.Position.Z,
                RotationX = collider.Rotation.X,
                RotationY = collider.Rotation.Y,
                RotationZ = collider.Rotation.Z,
                RotationW = collider.Rotation.W,
                HalfExtentsX = shape.HalfExtents.X,
                HalfExtentsY = shape.HalfExtents.Y,
                HalfExtentsZ = shape.HalfExtents.Z,
                Radius = shape.Radius,
                HalfHeight = shape.HalfHeight,
                Material = ToFile(collider.Material),
                Layer = (byte)collider.Layer,
                IsTrigger = collider.IsTrigger,
            };
            foreach (Vector3 point in shape.Points)
            {
                file.Points.Add(point.X);
                file.Points.Add(point.Y);
                file.Points.Add(point.Z);
            }

            foreach (int index in shape.Triangles)
            {
                file.Triangles.Add((uint)index);
            }

            return file;
        }

        public static SceneFileCollider2D ToFile(in ColliderDesc2D collider)
        {
            BodyShape2D shape = collider.Shape;
            var file = new SceneFileCollider2D
            {
                Kind = (byte)shape.Kind,
                PositionX = collider.Position.X,
                PositionY = collider.Position.Y,
                Rotation = collider.Rotation,
                HalfExtentsX = shape.HalfExtents.X,
                HalfExtentsY = shape.HalfExtents.Y,
                Radius = shape.Radius,
                HalfHeight = shape.HalfHeight,
                Material = ToFile(collider.Material),
                Layer = (byte)collider.Layer,
                IsTrigger = collider.IsTrigger,
            };
            foreach (Vector2 point in shape.Points)
            {
                file.Points.Add(point.X);
                file.Points.Add(point.Y);
            }

            return file;
        }

        public static SceneFileBody ToFile(in BodyDesc body)
        {
            var file = new SceneFileBody { Kind = (byte)(body.Kind + 1), Mass = body.Mass };
            foreach (ColliderDesc collider in body.Colliders)
            {
                file.Colliders.Add(ToFile(collider));
            }

            return file;
        }

        public static SceneFileBody2D ToFile(in BodyDesc2D body)
        {
            var file = new SceneFileBody2D { Kind = (byte)(body.Kind + 1), Mass = body.Mass, Rotation = body.Rotation };
            foreach (ColliderDesc2D collider in body.Colliders)
            {
                file.Colliders.Add(ToFile(collider));
            }

            return file;
        }

        public static ColliderDesc ToDesc(SceneFileCollider collider)
        {
            if (collider == null)
            {
                throw new ArgumentNullException(nameof(collider));
            }

            try
            {
                return new ColliderDesc(
                    ShapeOf(collider),
                    new Vector3(collider.PositionX, collider.PositionY, collider.PositionZ),
                    new Quaternion(collider.RotationX, collider.RotationY, collider.RotationZ, collider.RotationW),
                    MaterialOf(collider.Material),
                    collider.Layer,
                    collider.IsTrigger);
            }
            catch (ArgumentException exception)
            {
                throw Invalid("a collider", exception);
            }
        }

        public static ColliderDesc2D ToDesc(SceneFileCollider2D collider)
        {
            if (collider == null)
            {
                throw new ArgumentNullException(nameof(collider));
            }

            try
            {
                return new ColliderDesc2D(
                    ShapeOf(collider),
                    new Vector2(collider.PositionX, collider.PositionY),
                    collider.Rotation,
                    MaterialOf(collider.Material),
                    collider.Layer,
                    collider.IsTrigger);
            }
            catch (ArgumentException exception)
            {
                throw Invalid("a 2D collider", exception);
            }
        }

        public static bool TryGetBody(SceneFileObject entry, out BodyDesc body)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            SceneFileBody file = entry.Body;
            if (file == null || file.Kind == 0)
            {
                body = default;
                return false;
            }

            var colliders = new List<ColliderDesc>(file.Colliders.Count);
            foreach (SceneFileCollider collider in file.Colliders)
            {
                colliders.Add(ToDesc(collider));
            }

            SceneFilePose pose = entry.Pose ?? new SceneFilePose();
            try
            {
                body = new BodyDesc(
                    KindOf(file.Kind),
                    colliders,
                    new Vector3(pose.PositionX, pose.PositionY, pose.PositionZ),
                    new Quaternion(pose.RotationX, pose.RotationY, pose.RotationZ, pose.RotationW),
                    file.Mass);
            }
            catch (ArgumentException exception)
            {
                throw Invalid($"the body of scene object 0x{entry.SceneObjectId:X16}", exception);
            }

            return true;
        }

        public static bool TryGetBody2D(SceneFileObject entry, out BodyDesc2D body)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            SceneFileBody2D file = entry.Body2D;
            if (file == null || file.Kind == 0)
            {
                body = default;
                return false;
            }

            var colliders = new List<ColliderDesc2D>(file.Colliders.Count);
            foreach (SceneFileCollider2D collider in file.Colliders)
            {
                colliders.Add(ToDesc(collider));
            }

            SceneFilePose pose = entry.Pose ?? new SceneFilePose();
            try
            {
                body = new BodyDesc2D(KindOf(file.Kind), colliders, new Vector2(pose.PositionX, pose.PositionY), file.Rotation, file.Mass);
            }
            catch (ArgumentException exception)
            {
                throw Invalid($"the 2D body of scene object 0x{entry.SceneObjectId:X16}", exception);
            }

            return true;
        }

        private static SceneFileMaterial ToFile(in ColliderMaterial material) =>
            new SceneFileMaterial
            {
                Friction = material.Friction,
                Restitution = material.Restitution,
                FrictionCombine = (byte)material.FrictionCombine,
                RestitutionCombine = (byte)material.RestitutionCombine,
            };

        private static BodyShape ShapeOf(SceneFileCollider collider)
        {
            switch (collider.Kind)
            {
                case (byte)ShapeKind.Box:
                    return BodyShape.Box(new Vector3(collider.HalfExtentsX, collider.HalfExtentsY, collider.HalfExtentsZ));
                case (byte)ShapeKind.Sphere:
                    return BodyShape.Sphere(collider.Radius);
                case (byte)ShapeKind.Capsule:
                    return BodyShape.Capsule(collider.Radius, collider.HalfHeight);
                case (byte)ShapeKind.ConvexHull:
                    return BodyShape.ConvexHull(Points3D(collider.Points));
                case (byte)ShapeKind.TriangleMesh:
                    return BodyShape.TriangleMesh(Points3D(collider.Points), Indices(collider.Triangles));
                default:
                    throw new ArgumentException($"shape kind {collider.Kind} is unknown");
            }
        }

        private static BodyShape2D ShapeOf(SceneFileCollider2D collider)
        {
            switch (collider.Kind)
            {
                case (byte)ShapeKind2D.Box:
                    return BodyShape2D.Box(new Vector2(collider.HalfExtentsX, collider.HalfExtentsY));
                case (byte)ShapeKind2D.Circle:
                    return BodyShape2D.Circle(collider.Radius);
                case (byte)ShapeKind2D.Capsule:
                    return BodyShape2D.Capsule(collider.Radius, collider.HalfHeight);
                case (byte)ShapeKind2D.ConvexPolygon:
                    return BodyShape2D.ConvexPolygon(Points2D(collider.Points));
                case (byte)ShapeKind2D.Polyline:
                    return BodyShape2D.Polyline(Points2D(collider.Points));
                default:
                    throw new ArgumentException($"2D shape kind {collider.Kind} is unknown");
            }
        }

        private static ColliderMaterial MaterialOf(SceneFileMaterial material)
        {
            if (material == null)
            {
                throw new ArgumentException("the collider has no material");
            }

            return new ColliderMaterial(material.Friction, material.Restitution, RuleOf(material.FrictionCombine), RuleOf(material.RestitutionCombine));
        }

        private static CombineRule RuleOf(byte rule)
        {
            if (rule > (byte)CombineRule.Mean)
            {
                throw new ArgumentException($"combine rule {rule} is unknown");
            }

            return (CombineRule)rule;
        }

        private static BodyKind KindOf(byte kind)
        {
            if (kind > (byte)BodyKind.Static + 1)
            {
                throw new ArgumentException($"body kind {kind} is unknown");
            }

            return (BodyKind)(kind - 1);
        }

        private static Vector3[] Points3D(List<float> values)
        {
            if (values.Count % 3 != 0)
            {
                throw new ArgumentException($"{values.Count} coordinates do not make 3D points");
            }

            var points = new Vector3[values.Count / 3];
            for (int index = 0; index < points.Length; index++)
            {
                points[index] = new Vector3(values[index * 3], values[index * 3 + 1], values[index * 3 + 2]);
            }

            return points;
        }

        private static Vector2[] Points2D(List<float> values)
        {
            if (values.Count % 2 != 0)
            {
                throw new ArgumentException($"{values.Count} coordinates do not make 2D points");
            }

            var points = new Vector2[values.Count / 2];
            for (int index = 0; index < points.Length; index++)
            {
                points[index] = new Vector2(values[index * 2], values[index * 2 + 1]);
            }

            return points;
        }

        private static int[] Indices(List<uint> values)
        {
            var indices = new int[values.Count];
            for (int index = 0; index < indices.Length; index++)
            {
                if (values[index] > int.MaxValue)
                {
                    throw new ArgumentException($"triangle index {values[index]} is too large");
                }

                indices[index] = (int)values[index];
            }

            return indices;
        }

        private static InvalidDataException Invalid(string what, Exception inner) =>
            new InvalidDataException($"{what} in the scene file is not valid ({inner.Message}); export the scene again", inner);
    }
}
