using System;
using System.IO;
using System.Numerics;
using BundleFixture;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Simulation;
using NUnit.Framework;

namespace Fomoxa.Networking.Tests
{
    public sealed class SceneFileGeometryTest
    {
        private static readonly ColliderMaterial Slippery = new ColliderMaterial(0.1f, 0.7f, CombineRule.Minimum, CombineRule.Mean);
        private static readonly Vector3[] Tetrahedron = { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };

        [Test]
        public void CollidersOfEveryShapeGoToTheFileAndBack()
        {
            var rotation = Quaternion.CreateFromYawPitchRoll(0.5f, 0.25f, 0f);
            ColliderDesc[] colliders =
            {
                new ColliderDesc(BodyShape.Box(new Vector3(1f, 2f, 3f)), new Vector3(1f, 2f, 3f), rotation, Slippery, 7, true),
                new ColliderDesc(BodyShape.Sphere(0.5f), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 0, false),
                new ColliderDesc(BodyShape.Capsule(0.25f, 1f), Vector3.UnitY, Quaternion.Identity, ColliderMaterial.Default, 31, false),
                new ColliderDesc(BodyShape.ConvexHull(Tetrahedron), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 0, false),
                new ColliderDesc(BodyShape.TriangleMesh(Tetrahedron, new[] { 0, 1, 2, 0, 2, 3 }), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 0, false),
            };

            foreach (ColliderDesc collider in colliders)
            {
                ColliderDesc back = SceneFileGeometry.ToDesc(SceneFileGeometry.ToFile(collider));

                Assert.AreEqual((collider.Shape.Kind, collider.Shape.HalfExtents, collider.Shape.Radius, collider.Shape.HalfHeight), (back.Shape.Kind, back.Shape.HalfExtents, back.Shape.Radius, back.Shape.HalfHeight));
                CollectionAssert.AreEqual(collider.Shape.Points, back.Shape.Points);
                CollectionAssert.AreEqual(collider.Shape.Triangles, back.Shape.Triangles);
                Assert.AreEqual((collider.Position, collider.Rotation, collider.Layer, collider.IsTrigger), (back.Position, back.Rotation, back.Layer, back.IsTrigger));
                Assert.AreEqual(collider.Material, back.Material);
            }
        }

        [Test]
        public void TwoDimensionalCollidersOfEveryShapeGoToTheFileAndBack()
        {
            Vector2[] triangle = { Vector2.Zero, Vector2.UnitX, Vector2.UnitY };
            ColliderDesc2D[] colliders =
            {
                new ColliderDesc2D(BodyShape2D.Box(new Vector2(1f, 2f)), new Vector2(1f, 2f), 0.5f, Slippery, 3, true),
                new ColliderDesc2D(BodyShape2D.Circle(0.5f), Vector2.Zero, 0f, ColliderMaterial.Default, 0, false),
                new ColliderDesc2D(BodyShape2D.Capsule(0.25f, 1f), Vector2.UnitY, 1f, ColliderMaterial.Default, 0, false),
                new ColliderDesc2D(BodyShape2D.ConvexPolygon(triangle), Vector2.Zero, 0f, ColliderMaterial.Default, 0, false),
                new ColliderDesc2D(BodyShape2D.Polyline(triangle), Vector2.Zero, 0f, ColliderMaterial.Default, 0, false),
            };

            foreach (ColliderDesc2D collider in colliders)
            {
                ColliderDesc2D back = SceneFileGeometry.ToDesc(SceneFileGeometry.ToFile(collider));

                Assert.AreEqual((collider.Shape.Kind, collider.Shape.HalfExtents, collider.Shape.Radius, collider.Shape.HalfHeight), (back.Shape.Kind, back.Shape.HalfExtents, back.Shape.Radius, back.Shape.HalfHeight));
                CollectionAssert.AreEqual(collider.Shape.Points, back.Shape.Points);
                Assert.AreEqual((collider.Position, collider.Rotation, collider.Layer, collider.IsTrigger), (back.Position, back.Rotation, back.Layer, back.IsTrigger));
                Assert.AreEqual(collider.Material, back.Material);
            }
        }

        [Test]
        public void BodiesOfASceneObjectComeBackAtThePoseOfTheObject()
        {
            var body = new BodyDesc(BodyKind.Kinematic, new[] { new ColliderDesc(BodyShape.Sphere(1f), Vector3.UnitY, Quaternion.Identity, Slippery, 2, false) }, Vector3.Zero, Quaternion.Identity, 3f);
            var body2D = new BodyDesc2D(BodyKind.Dynamic, BodyShape2D.Circle(0.5f), Vector2.Zero, 1.25f, 2f);
            var entry = new SceneFileObject
            {
                SceneObjectId = 0x10,
                Pose = new SceneFilePose { PositionX = 1f, PositionY = 2f, PositionZ = 3f, RotationW = 1f, ScaleX = 4f, ScaleY = 4f, ScaleZ = 4f },
                Body = SceneFileGeometry.ToFile(body),
                Body2D = SceneFileGeometry.ToFile(body2D),
            };

            Assert.IsTrue(SceneFileGeometry.TryGetBody(entry, out BodyDesc back));
            Assert.IsTrue(SceneFileGeometry.TryGetBody2D(entry, out BodyDesc2D back2D));

            Assert.AreEqual((BodyKind.Kinematic, 3f, new Vector3(1f, 2f, 3f), Quaternion.Identity), (back.Kind, back.Mass, back.Position, back.Rotation));
            Assert.AreEqual((1, Vector3.UnitY, 2), (back.Colliders.Count, back.Colliders[0].Position, back.Colliders[0].Layer));
            Assert.AreEqual((BodyKind.Dynamic, 2f, new Vector2(1f, 2f), 1.25f), (back2D.Kind, back2D.Mass, back2D.Position, back2D.Rotation));
            Assert.AreEqual(ShapeKind2D.Circle, back2D.Colliders[0].Shape.Kind);
        }

        [Test]
        public void AnEntryWithoutABodyHasNone()
        {
            var entry = new SceneFileObject();

            Assert.IsFalse(SceneFileGeometry.TryGetBody(entry, out _));
            Assert.IsFalse(SceneFileGeometry.TryGetBody2D(entry, out _));
            Assert.Throws<ArgumentNullException>(() => SceneFileGeometry.TryGetBody(null, out _));
        }

        [Test]
        public void BrokenGeometryAsksForANewExport()
        {
            SceneFileCollider Valid() => SceneFileGeometry.ToFile(new ColliderDesc(BodyShape.Sphere(1f), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 0, false));
            SceneFileCollider unknownKind = Valid();
            unknownKind.Kind = 9;
            SceneFileCollider strayCoordinate = SceneFileGeometry.ToFile(new ColliderDesc(BodyShape.ConvexHull(Tetrahedron), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 0, false));
            strayCoordinate.Points.Add(1f);
            SceneFileCollider unknownRule = Valid();
            unknownRule.Material.FrictionCombine = 7;
            SceneFileCollider highLayer = Valid();
            highLayer.Layer = 40;
            SceneFileCollider badIndex = SceneFileGeometry.ToFile(new ColliderDesc(BodyShape.TriangleMesh(Tetrahedron, new[] { 0, 1, 2 }), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 0, false));
            badIndex.Triangles[2] = 4;
            var movingMesh = new SceneFileObject { Body = new SceneFileBody { Kind = (byte)(BodyKind.Dynamic + 1), Colliders = { SceneFileGeometry.ToFile(new ColliderDesc(BodyShape.TriangleMesh(Tetrahedron, new[] { 0, 1, 2 }), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 0, false)) } } };
            var unknownBody = new SceneFileObject { Body2D = new SceneFileBody2D { Kind = 5 } };

            foreach (SceneFileCollider broken in new[] { unknownKind, strayCoordinate, unknownRule, highLayer, badIndex })
            {
                InvalidDataException exception = Assert.Throws<InvalidDataException>(() => SceneFileGeometry.ToDesc(broken));
                StringAssert.Contains("export the scene again", exception.Message);
            }

            Assert.Throws<InvalidDataException>(() => SceneFileGeometry.TryGetBody(movingMesh, out _));
            Assert.Throws<InvalidDataException>(() => SceneFileGeometry.TryGetBody2D(unknownBody, out _));
        }

        [Test]
        public void ASceneFileWithGeometryAndLayerMatricesSurvivesTheCodec()
        {
            var file = new SceneFile { SceneId = 0x55 };
            file.Colliders.Add(SceneFileGeometry.ToFile(new ColliderDesc(BodyShape.TriangleMesh(Tetrahedron, new[] { 0, 1, 2 }), Vector3.UnitX, Quaternion.Identity, Slippery, 4, false)));
            file.Colliders2D.Add(SceneFileGeometry.ToFile(new ColliderDesc2D(BodyShape2D.Polyline(new[] { Vector2.Zero, Vector2.UnitX }), Vector2.Zero, 0f, ColliderMaterial.Default, 1, true)));
            for (int layer = 0; layer < 32; layer++)
            {
                file.LayerCollisions.Add(~(1u << layer));
                file.LayerCollisions2D.Add(uint.MaxValue);
            }

            file.Objects.Add(new SceneFileObject { SceneObjectId = 1, Body2D = SceneFileGeometry.ToFile(new BodyDesc2D(BodyKind.Static, BodyShape2D.Box(Vector2.One), Vector2.Zero, 0.5f, 0f)) });

            SceneFile read = SceneFileFormat.Read(TestObjects.Registry(), SceneFileFormat.Write(TestObjects.Registry(), file));

            ColliderDesc mesh = SceneFileGeometry.ToDesc(read.Colliders[0]);
            Assert.AreEqual((ShapeKind.TriangleMesh, 4, 3, 4), (mesh.Shape.Kind, mesh.Shape.Points.Count, mesh.Shape.Triangles.Count, mesh.Layer));
            Assert.AreEqual(Slippery, mesh.Material);
            ColliderDesc2D line = SceneFileGeometry.ToDesc(read.Colliders2D[0]);
            Assert.AreEqual((ShapeKind2D.Polyline, 2, true), (line.Shape.Kind, line.Shape.Points.Count, line.IsTrigger));
            CollectionAssert.AreEqual(file.LayerCollisions, read.LayerCollisions);
            CollectionAssert.AreEqual(file.LayerCollisions2D, read.LayerCollisions2D);
            Assert.IsTrue(SceneFileGeometry.TryGetBody2D(read.Objects[0], out BodyDesc2D body));
            Assert.AreEqual((BodyKind.Static, 0.5f), (body.Kind, body.Rotation));
            Assert.IsFalse(SceneFileGeometry.TryGetBody(read.Objects[0], out _));
        }
    }
}
