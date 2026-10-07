using System;
using System.Collections.Generic;
using Fomoxa.Networking.Simulation;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity.Tests
{
    public sealed class BodyDescriptionsTest
    {
        private const float Tolerance = 1e-5f;

        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
        private Scene scene;

        [SetUp]
        public void CreateScene() => scene = EditorSceneManager.NewPreviewScene();

        [TearDown]
        public void DestroyCreated()
        {
            foreach (UnityEngine.Object item in created)
            {
                if (item != null)
                {
                    UnityEngine.Object.DestroyImmediate(item);
                }
            }

            created.Clear();
            EditorSceneManager.ClosePreviewScene(scene);
        }

        [Test]
        public void ARigidbodyAndItsChildCollidersBecomeOneBodyWithLocalPoses()
        {
            GameObject root = Create("Root");
            root.transform.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 90f, 0f));
            root.AddComponent<SphereCollider>().radius = 0.5f;
            var rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.mass = 3f;
            GameObject child = Child(root, "Box", new Vector3(0f, 1f, 0f));
            child.transform.localScale = new Vector3(2f, 1f, 1f);
            var box = child.AddComponent<BoxCollider>();
            box.size = new Vector3(2f, 2f, 2f);
            box.center = new Vector3(0f, 0.5f, 0f);

            Assert.IsTrue(BodyDescriptions.TryDescribe(root, out BodyDesc desc));

            Assert.AreEqual((BodyKind.Dynamic, 3f), (desc.Kind, desc.Mass));
            AssertNear(new Vector3(1f, 2f, 3f), desc.Position);
            Assert.AreEqual(2, desc.Colliders.Count);
            Assert.AreEqual(ShapeKind.Sphere, desc.Colliders[0].Shape.Kind);
            Assert.AreEqual(0.5f, desc.Colliders[0].Shape.Radius, Tolerance);
            AssertNear(Vector3.zero, desc.Colliders[0].Position);
            Assert.AreEqual(ShapeKind.Box, desc.Colliders[1].Shape.Kind);
            AssertNear(new Vector3(2f, 1f, 1f), desc.Colliders[1].Shape.HalfExtents);
            AssertNear(new Vector3(0f, 1.5f, 0f), desc.Colliders[1].Position);
            AssertNear(Quaternion.identity, desc.Colliders[1].Rotation);
        }

        [Test]
        public void CollidersOfANestedRigidbodyAndDisabledCollidersAreLeftOut()
        {
            GameObject root = Create("Root");
            root.AddComponent<Rigidbody>().isKinematic = true;
            root.AddComponent<BoxCollider>();
            GameObject nested = Child(root, "Nested", Vector3.right);
            nested.AddComponent<Rigidbody>();
            nested.AddComponent<SphereCollider>();
            Child(root, "Disabled", Vector3.left).AddComponent<SphereCollider>().enabled = false;

            Assert.IsTrue(BodyDescriptions.TryDescribe(root, out BodyDesc desc));

            Assert.AreEqual(BodyKind.Kinematic, desc.Kind);
            Assert.AreEqual(1, desc.Colliders.Count);
            Assert.AreEqual(ShapeKind.Box, desc.Colliders[0].Shape.Kind);
        }

        [Test]
        public void AnObjectWithoutRigidbodyIsStaticAndOneWithoutCollidersHasNoBody()
        {
            GameObject wall = Create("Wall");
            wall.AddComponent<BoxCollider>();
            GameObject empty = Create("Empty");

            Assert.IsTrue(BodyDescriptions.TryDescribe(wall, out BodyDesc desc));
            Assert.AreEqual((BodyKind.Static, 0f), (desc.Kind, desc.Mass));
            Assert.IsFalse(BodyDescriptions.TryDescribe(empty, out _));
            Assert.IsFalse(BodyDescriptions.TryDescribe2D(empty, out _));
            Assert.Throws<ArgumentNullException>(() => BodyDescriptions.TryDescribe(null, out _));
        }

        [Test]
        public void SpheresAndCapsulesFollowTheScaleRulesOfUnity()
        {
            GameObject root = Create("Root");
            GameObject ball = Child(root, "Ball", Vector3.zero);
            ball.transform.localScale = new Vector3(1f, -2f, 3f);
            ball.AddComponent<SphereCollider>().radius = 1f;
            GameObject pill = Child(root, "Pill", Vector3.zero);
            pill.transform.localScale = new Vector3(2f, 1f, 3f);
            var capsule = pill.AddComponent<CapsuleCollider>();
            capsule.direction = 0;
            capsule.radius = 0.5f;
            capsule.height = 3f;

            Assert.IsTrue(BodyDescriptions.TryDescribe(root, out BodyDesc desc));

            Assert.AreEqual(3f, desc.Colliders[0].Shape.Radius, Tolerance);
            BodyShape shape = desc.Colliders[1].Shape;
            Assert.AreEqual(ShapeKind.Capsule, shape.Kind);
            Assert.AreEqual(1.5f, shape.Radius, Tolerance);
            Assert.AreEqual(1.5f, shape.HalfHeight, Tolerance);
            Vector3 axis = desc.Colliders[1].Rotation.ToUnity() * Vector3.up;
            Assert.AreEqual(1f, Mathf.Abs(axis.x), Tolerance);
        }

        [Test]
        public void MaterialLayerAndTriggerAreCarriedAndAMissingMaterialIsTheDefaultOfUnity()
        {
            GameObject root = Create("Root");
            var material = new PhysicsMaterial
            {
                dynamicFriction = 0.3f,
                staticFriction = 0.9f,
                bounciness = 0.4f,
                frictionCombine = PhysicsMaterialCombine.Maximum,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };
            created.Add(material);
            var trigger = root.AddComponent<BoxCollider>();
            trigger.sharedMaterial = material;
            trigger.isTrigger = true;
            GameObject plain = Child(root, "Plain", Vector3.zero);
            plain.layer = 5;
            plain.AddComponent<SphereCollider>();

            Assert.IsTrue(BodyDescriptions.TryDescribe(root, out BodyDesc desc));

            ColliderDesc first = desc.Colliders[0];
            Assert.AreEqual((0.3f, 0.4f, CombineRule.Maximum, CombineRule.Minimum, true), (first.Material.Friction, first.Material.Restitution, first.Material.FrictionCombine, first.Material.RestitutionCombine, first.IsTrigger));
            ColliderDesc second = desc.Colliders[1];
            Assert.AreEqual((5, false), (second.Layer, second.IsTrigger));
            Assert.AreEqual((0.6f, 0f, CombineRule.Average, CombineRule.Average), (second.Material.Friction, second.Material.Restitution, second.Material.FrictionCombine, second.Material.RestitutionCombine));
        }

        [Test]
        public void MeshCollidersBecomeHullsOrStaticMeshes()
        {
            Mesh mesh = Tetrahedron();
            GameObject wall = Create("Wall");
            wall.transform.localScale = new Vector3(2f, 1f, 1f);
            wall.AddComponent<MeshCollider>().sharedMesh = mesh;
            GameObject rock = Create("Rock");
            rock.AddComponent<Rigidbody>();
            var hull = rock.AddComponent<MeshCollider>();
            hull.sharedMesh = mesh;
            hull.convex = true;
            GameObject moving = Create("Moving");
            moving.AddComponent<Rigidbody>().isKinematic = true;
            moving.AddComponent<MeshCollider>().sharedMesh = mesh;

            Assert.IsTrue(BodyDescriptions.TryDescribe(wall, out BodyDesc wallDesc));
            Assert.IsTrue(BodyDescriptions.TryDescribe(rock, out BodyDesc rockDesc));

            BodyShape triangles = wallDesc.Colliders[0].Shape;
            Assert.AreEqual((ShapeKind.TriangleMesh, 4, 12), (triangles.Kind, triangles.Points.Count, triangles.Triangles.Count));
            AssertNear(new Vector3(2f, 0f, 0f), triangles.Points[1]);
            Assert.AreEqual((ShapeKind.ConvexHull, 4), (rockDesc.Colliders[0].Shape.Kind, rockDesc.Colliders[0].Shape.Points.Count));
            Assert.Throws<NotSupportedException>(() => BodyDescriptions.TryDescribe(moving, out _));
        }

        [Test]
        public void UnsupportedCollidersAndLayerOverridesThrow()
        {
            GameObject wheel = Create("Wheel");
            wheel.AddComponent<Rigidbody>();
            wheel.AddComponent<WheelCollider>();
            GameObject filtered = Create("Filtered");
            filtered.AddComponent<BoxCollider>().excludeLayers = 1 << 3;
            GameObject rounded = Create("Rounded");
            rounded.AddComponent<BoxCollider2D>().edgeRadius = 0.1f;

            Assert.Throws<NotSupportedException>(() => BodyDescriptions.TryDescribe(wheel, out _));
            Assert.Throws<NotSupportedException>(() => BodyDescriptions.TryDescribe(filtered, out _));
            Assert.Throws<NotSupportedException>(() => BodyDescriptions.TryDescribe2D(rounded, out _));
        }

        [Test]
        public void TwoDimensionalCollidersBecomeOneBodyWithLocalPosesAndAngles()
        {
            GameObject root = Create("Root");
            root.transform.SetPositionAndRotation(new Vector3(1f, 2f, 0f), Quaternion.Euler(0f, 0f, 90f));
            var rigidbody = root.AddComponent<Rigidbody2D>();
            rigidbody.mass = 2f;
            var box = root.AddComponent<BoxCollider2D>();
            box.size = new Vector2(2f, 1f);
            box.offset = new Vector2(0.5f, 0f);
            GameObject child = Child(root, "Pill", new Vector3(0f, 1f, 0f));
            child.transform.localScale = new Vector3(2f, 2f, 1f);
            var capsule = child.AddComponent<CapsuleCollider2D>();
            capsule.direction = CapsuleDirection2D.Horizontal;
            capsule.size = new Vector2(2f, 1f);
            Child(root, "Ball", new Vector3(-1f, 0f, 0f)).AddComponent<CircleCollider2D>().radius = 0.25f;

            Assert.IsTrue(BodyDescriptions.TryDescribe2D(root, out BodyDesc2D desc));

            Assert.AreEqual((BodyKind.Dynamic, 2f), (desc.Kind, desc.Mass));
            Assert.AreEqual(Mathf.PI * 0.5f, desc.Rotation, Tolerance);
            Assert.AreEqual(3, desc.Colliders.Count);
            AssertNear(new Vector2(1f, 0.5f), desc.Colliders[0].Shape.HalfExtents);
            AssertNear(new Vector2(0.5f, 0f), desc.Colliders[0].Position);
            BodyShape2D pill = desc.Colliders[1].Shape;
            Assert.AreEqual(ShapeKind2D.Capsule, pill.Kind);
            Assert.AreEqual(1f, pill.Radius, Tolerance);
            Assert.AreEqual(1f, pill.HalfHeight, Tolerance);
            AssertNear(new Vector2(0f, 1f), desc.Colliders[1].Position);
            Assert.AreEqual(Mathf.PI * 0.5f, desc.Colliders[1].Rotation, Tolerance);
            Assert.AreEqual(0.25f, desc.Colliders[2].Shape.Radius, Tolerance);
            AssertNear(new Vector2(-1f, 0f), desc.Colliders[2].Position);
        }

        [Test]
        public void AConcavePolygonIsSplitOnAMovingBodyAndOutlinedOnAStaticOne()
        {
            Vector2[] lShape = { new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(2f, 1f), new Vector2(1f, 1f), new Vector2(1f, 2f), new Vector2(0f, 2f) };
            GameObject moving = Create("Moving");
            moving.AddComponent<Rigidbody2D>();
            moving.AddComponent<PolygonCollider2D>().points = lShape;
            GameObject ground = Create("Ground");
            ground.AddComponent<PolygonCollider2D>().points = lShape;

            Assert.IsTrue(BodyDescriptions.TryDescribe2D(moving, out BodyDesc2D movingDesc));
            Assert.IsTrue(BodyDescriptions.TryDescribe2D(ground, out BodyDesc2D groundDesc));

            Assert.AreEqual(2, movingDesc.Colliders.Count);
            Assert.AreEqual(ShapeKind2D.ConvexPolygon, movingDesc.Colliders[0].Shape.Kind);
            Assert.AreEqual(ShapeKind2D.ConvexPolygon, movingDesc.Colliders[1].Shape.Kind);
            Assert.AreEqual(1, groundDesc.Colliders.Count);
            BodyShape2D outline = groundDesc.Colliders[0].Shape;
            Assert.AreEqual((BodyKind.Static, ShapeKind2D.Polyline, 7), (groundDesc.Kind, outline.Kind, outline.Points.Count));
            Assert.AreEqual(outline.Points[0], outline.Points[6]);
        }

        [Test]
        public void EdgesAreStaticOnlyAndTwoDimensionalMaterialsKeepTheMeanRule()
        {
            var material = new PhysicsMaterial2D
            {
                friction = 0.2f,
                bounciness = 0.1f,
                frictionCombine = PhysicsMaterialCombine2D.Mean,
                bounceCombine = PhysicsMaterialCombine2D.Maximum,
            };
            created.Add(material);
            GameObject ground = Create("Ground");
            ground.AddComponent<EdgeCollider2D>().points = new[] { Vector2.zero, Vector2.right, new Vector2(2f, 1f) };
            GameObject moving = Create("Moving");
            moving.AddComponent<Rigidbody2D>().sharedMaterial = material;
            moving.AddComponent<CircleCollider2D>();
            GameObject rail = Create("Rail");
            rail.AddComponent<Rigidbody2D>();
            rail.AddComponent<EdgeCollider2D>();

            Assert.IsTrue(BodyDescriptions.TryDescribe2D(ground, out BodyDesc2D groundDesc));
            Assert.IsTrue(BodyDescriptions.TryDescribe2D(moving, out BodyDesc2D movingDesc));

            Assert.AreEqual((ShapeKind2D.Polyline, 3), (groundDesc.Colliders[0].Shape.Kind, groundDesc.Colliders[0].Shape.Points.Count));
            ColliderMaterial carried = movingDesc.Colliders[0].Material;
            Assert.AreEqual((0.2f, 0.1f, CombineRule.Mean, CombineRule.Maximum), (carried.Friction, carried.Restitution, carried.FrictionCombine, carried.RestitutionCombine));
            Assert.Throws<NotSupportedException>(() => BodyDescriptions.TryDescribe2D(rail, out _));
        }

        [Test]
        public void StaticCollidersAreDescribedInTheGivenSpace()
        {
            GameObject space = Create("Space");
            space.transform.position = new Vector3(10f, 0f, 0f);
            GameObject wall = Create("Wall");
            wall.transform.position = new Vector3(12f, 1f, 0f);
            var box = wall.AddComponent<BoxCollider>();
            GameObject floor = Create("Floor");
            floor.transform.position = new Vector3(12f, 1f, 0f);
            var polygon = floor.AddComponent<PolygonCollider2D>();
            var into = new List<ColliderDesc>();
            var into2D = new List<ColliderDesc2D>();

            BodyDescriptions.DescribeStatic(box, space.transform, into);
            BodyDescriptions.DescribeStatic(box, null, into);
            BodyDescriptions.DescribeStatic2D(polygon, space.transform, into2D);

            AssertNear(new Vector3(2f, 1f, 0f), into[0].Position);
            AssertNear(new Vector3(12f, 1f, 0f), into[1].Position);
            Assert.AreEqual(ShapeKind2D.Polyline, into2D[0].Shape.Kind);
            Assert.Throws<ArgumentNullException>(() => BodyDescriptions.DescribeStatic(box, null, null));
        }

        private GameObject Create(string name)
        {
            var gameObject = new GameObject(name);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            created.Add(gameObject);
            return gameObject;
        }

        private static GameObject Child(GameObject parent, string name, Vector3 localPosition)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            child.transform.localPosition = localPosition;
            return child;
        }

        private Mesh Tetrahedron()
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward },
                triangles = new[] { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3 },
            };
            created.Add(mesh);
            return mesh;
        }

        private static void AssertNear(Vector3 expected, System.Numerics.Vector3 actual)
        {
            Assert.AreEqual(expected.x, actual.X, Tolerance);
            Assert.AreEqual(expected.y, actual.Y, Tolerance);
            Assert.AreEqual(expected.z, actual.Z, Tolerance);
        }

        private static void AssertNear(Vector2 expected, System.Numerics.Vector2 actual)
        {
            Assert.AreEqual(expected.x, actual.X, Tolerance);
            Assert.AreEqual(expected.y, actual.Y, Tolerance);
        }

        private static void AssertNear(Quaternion expected, System.Numerics.Quaternion actual)
        {
            Assert.AreEqual(1f, Mathf.Abs(Quaternion.Dot(expected, actual.ToUnity())), Tolerance);
        }
    }
}
