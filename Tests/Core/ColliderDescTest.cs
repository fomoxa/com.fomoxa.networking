using System;
using System.Numerics;
using Fomoxa.Networking.Simulation;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class ColliderDescTest
    {
        private static readonly Vector3[] Tetrahedron = { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };

        [Test]
        public void ASingleShapeBodyHasOneDefaultColliderAtItsOrigin()
        {
            var body = new BodyDesc(BodyKind.Dynamic, BodyShape.Sphere(0.5f), Vector3.One, Quaternion.Identity, 2f);
            var body2D = new BodyDesc2D(BodyKind.Dynamic, BodyShape2D.Circle(0.5f), Vector2.One, 0.5f, 2f);

            Assert.AreEqual(1, body.Colliders.Count);
            ColliderDesc collider = body.Colliders[0];
            Assert.AreEqual((ShapeKind.Sphere, 0.5f), (collider.Shape.Kind, collider.Shape.Radius));
            Assert.AreEqual((Vector3.Zero, Quaternion.Identity, 0, false), (collider.Position, collider.Rotation, collider.Layer, collider.IsTrigger));
            Assert.AreEqual(ColliderMaterial.Default, collider.Material);
            Assert.AreEqual(1, body2D.Colliders.Count);
            ColliderDesc2D collider2D = body2D.Colliders[0];
            Assert.AreEqual((ShapeKind2D.Circle, Vector2.Zero, 0f, 0, false), (collider2D.Shape.Kind, collider2D.Position, collider2D.Rotation, collider2D.Layer, collider2D.IsTrigger));
            Assert.AreEqual(ColliderMaterial.Default, collider2D.Material);
        }

        [Test]
        public void ABodyKeepsItsCollidersInOrder()
        {
            var material = new ColliderMaterial(0.2f, 0.5f, CombineRule.Mean, CombineRule.Maximum);
            ColliderDesc[] colliders =
            {
                new ColliderDesc(BodyShape.Box(Vector3.One), new Vector3(0f, 1f, 0f), Quaternion.Identity, material, 3, false),
                new ColliderDesc(BodyShape.ConvexHull(Tetrahedron), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 4, true),
            };

            var body = new BodyDesc(BodyKind.Kinematic, colliders, Vector3.Zero, Quaternion.Identity, 1f);
            colliders[0] = colliders[1];

            Assert.AreEqual(2, body.Colliders.Count);
            Assert.AreEqual((ShapeKind.Box, new Vector3(0f, 1f, 0f), 3, false), (body.Colliders[0].Shape.Kind, body.Colliders[0].Position, body.Colliders[0].Layer, body.Colliders[0].IsTrigger));
            Assert.AreEqual((0.2f, 0.5f, CombineRule.Mean, CombineRule.Maximum), (body.Colliders[0].Material.Friction, body.Colliders[0].Material.Restitution, body.Colliders[0].Material.FrictionCombine, body.Colliders[0].Material.RestitutionCombine));
            Assert.AreEqual((ShapeKind.ConvexHull, 4, true), (body.Colliders[1].Shape.Kind, body.Colliders[1].Layer, body.Colliders[1].IsTrigger));
        }

        [Test]
        public void ShapesCopyThePointsTheyAreGiven()
        {
            var points = (Vector3[])Tetrahedron.Clone();
            var vertices = new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY };
            var triangles = new[] { 0, 1, 2 };
            var polygon = new[] { Vector2.Zero, Vector2.UnitX, Vector2.One };

            BodyShape hull = BodyShape.ConvexHull(points);
            BodyShape mesh = BodyShape.TriangleMesh(vertices, triangles);
            BodyShape2D convex = BodyShape2D.ConvexPolygon(polygon);
            BodyShape2D line = BodyShape2D.Polyline(polygon);
            points[0] = new Vector3(9f);
            vertices[0] = new Vector3(9f);
            triangles[0] = 2;
            polygon[0] = new Vector2(9f);

            Assert.AreEqual(Vector3.Zero, hull.Points[0]);
            Assert.AreEqual(Vector3.Zero, mesh.Points[0]);
            Assert.AreEqual(0, mesh.Triangles[0]);
            Assert.AreEqual(Vector2.Zero, convex.Points[0]);
            Assert.AreEqual(Vector2.Zero, line.Points[0]);
            Assert.AreEqual(3, line.Points.Count);
            Assert.IsEmpty(BodyShape.Box(Vector3.One).Points);
            Assert.IsEmpty(default(BodyShape).Triangles);
            Assert.IsEmpty(default(BodyDesc).Colliders);
            Assert.IsEmpty(default(BodyDesc2D).Colliders);
        }

        [Test]
        public void ShapesWithTooFewPointsOrBadIndicesAreRefused()
        {
            Vector3[] triangle = { Vector3.Zero, Vector3.UnitX, Vector3.UnitY };

            Assert.Throws<ArgumentNullException>(() => BodyShape.ConvexHull(null));
            Assert.Throws<ArgumentException>(() => BodyShape.ConvexHull(triangle));
            Assert.Throws<ArgumentException>(() => BodyShape.TriangleMesh(new[] { Vector3.Zero, Vector3.UnitX }, new[] { 0, 1, 1 }));
            Assert.Throws<ArgumentException>(() => BodyShape.TriangleMesh(triangle, new[] { 0, 1 }));
            Assert.Throws<ArgumentException>(() => BodyShape.TriangleMesh(triangle, new[] { 0, 1, 3 }));
            Assert.Throws<ArgumentException>(() => BodyShape.TriangleMesh(triangle, new[] { 0, 1, -1 }));
            Assert.Throws<ArgumentException>(() => BodyShape2D.ConvexPolygon(new[] { Vector2.Zero, Vector2.UnitX }));
            Assert.Throws<ArgumentException>(() => BodyShape2D.Polyline(new[] { Vector2.Zero }));
        }

        [Test]
        public void MeshesAndPolylinesNeedAStaticBody()
        {
            BodyShape mesh = BodyShape.TriangleMesh(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }, new[] { 0, 1, 2 });
            BodyShape2D line = BodyShape2D.Polyline(new[] { Vector2.Zero, Vector2.UnitX });

            Assert.Throws<ArgumentException>(() => new BodyDesc(BodyKind.Dynamic, mesh, Vector3.Zero, Quaternion.Identity, 1f));
            Assert.Throws<ArgumentException>(() => new BodyDesc(BodyKind.Kinematic, mesh, Vector3.Zero, Quaternion.Identity, 1f));
            Assert.Throws<ArgumentException>(() => new BodyDesc2D(BodyKind.Dynamic, line, Vector2.Zero, 0f, 1f));
            Assert.DoesNotThrow(() => new BodyDesc(BodyKind.Static, mesh, Vector3.Zero, Quaternion.Identity, 0f));
            Assert.DoesNotThrow(() => new BodyDesc2D(BodyKind.Static, line, Vector2.Zero, 0f, 0f));
        }

        [Test]
        public void ABodyNeedsAColliderAndALayerFrom0To31()
        {
            Assert.Throws<ArgumentException>(() => new BodyDesc(BodyKind.Dynamic, Array.Empty<ColliderDesc>(), Vector3.Zero, Quaternion.Identity, 1f));
            Assert.Throws<ArgumentNullException>(() => new BodyDesc2D(BodyKind.Dynamic, null, Vector2.Zero, 0f, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ColliderDesc(BodyShape.Sphere(1f), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 32, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ColliderDesc2D(BodyShape2D.Circle(1f), Vector2.Zero, 0f, ColliderMaterial.Default, -1, false));
            Assert.DoesNotThrow(() => new ColliderDesc2D(BodyShape2D.Circle(1f), Vector2.Zero, 0f, ColliderMaterial.Default, 31, false));
        }

        [Test]
        public void AMaterialRefusesNegativeValuesAndDefaultsToUnity3D()
        {
            Assert.Throws<ArgumentException>(() => new ColliderMaterial(-0.1f, 0f, CombineRule.Average, CombineRule.Average));
            Assert.Throws<ArgumentException>(() => new ColliderMaterial(0f, float.NaN, CombineRule.Average, CombineRule.Average));
            Assert.AreEqual((0.6f, 0f, CombineRule.Average, CombineRule.Average), (ColliderMaterial.Default.Friction, ColliderMaterial.Default.Restitution, ColliderMaterial.Default.FrictionCombine, ColliderMaterial.Default.RestitutionCombine));
        }
    }
}
