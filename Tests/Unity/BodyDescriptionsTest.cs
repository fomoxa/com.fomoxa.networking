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

        [Test]
        public void SourcesRunParallelToTheCollidersAndPiecesShareTheirPolygon()
        {
            GameObject root = Create("Root");
            root.AddComponent<Rigidbody>();
            var sphere = root.AddComponent<SphereCollider>();
            var box = Child(root, "Box", Vector3.up).AddComponent<BoxCollider>();
            GameObject moving = Create("Moving");
            moving.AddComponent<Rigidbody2D>();
            var circle = moving.AddComponent<CircleCollider2D>();
            var polygon = moving.AddComponent<PolygonCollider2D>();
            polygon.points = new[] { new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(2f, 1f), new Vector2(1f, 1f), new Vector2(1f, 2f), new Vector2(0f, 2f) };
            var sources = new List<Collider> { box };
            var sources2D = new List<Collider2D>();

            Assert.IsTrue(BodyDescriptions.TryDescribe(root, out BodyDesc desc, sources));
            Assert.IsTrue(BodyDescriptions.TryDescribe2D(moving, out BodyDesc2D desc2D, sources2D));

            CollectionAssert.AreEqual(new Collider[] { sphere, box }, sources);
            Assert.AreEqual(desc.Colliders.Count, sources.Count);
            CollectionAssert.AreEqual(new Collider2D[] { circle, polygon, polygon }, sources2D);
            Assert.AreEqual(desc2D.Colliders.Count, sources2D.Count);
            Assert.Throws<ArgumentNullException>(() => BodyDescriptions.TryDescribe(root, out _, null));
            Assert.Throws<ArgumentNullException>(() => BodyDescriptions.TryDescribe2D(moving, out _, null));
        }

        [Test]
        public void StaticCollidersListOneEntryPerSceneFileColliderInSceneOrder()
        {
            var wall = Create("Wall").AddComponent<BoxCollider>();
            GameObject moving = Create("Moving");
            moving.AddComponent<Rigidbody>();
            moving.AddComponent<SphereCollider>();
            GameObject door = Create("Door");
            door.AddComponent<NetworkObject>();
            Child(door, "Frame", Vector3.zero).AddComponent<BoxCollider>();
            Create("Off").AddComponent<BoxCollider>().enabled = false;
            GameObject hidden = Create("Hidden");
            hidden.AddComponent<BoxCollider>();
            hidden.SetActive(false);
            var step = Child(Create("Floor"), "Step", Vector3.zero).AddComponent<SphereCollider>();
            var ground = Create("Ground").AddComponent<PolygonCollider2D>();
            ground.pathCount = 2;
            ground.SetPath(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f) });
            ground.SetPath(1, new[] { new Vector2(5f, 0f), new Vector2(6f, 0f), new Vector2(5f, 1f) });
            var coin = Create("Coin").AddComponent<CircleCollider2D>();
            var into = new List<Collider> { wall };
            var into2D = new List<Collider2D>();

            BodyDescriptions.StaticColliders(scene, into);
            BodyDescriptions.StaticColliders2D(scene, into2D);

            CollectionAssert.AreEqual(new Collider[] { wall, step }, into);
            CollectionAssert.AreEqual(new Collider2D[] { ground, ground, coin }, into2D);
            Assert.Throws<ArgumentNullException>(() => BodyDescriptions.StaticColliders(scene, null));
            Assert.Throws<ArgumentException>(() => BodyDescriptions.StaticColliders(default(Scene), into));
            Assert.Throws<ArgumentNullException>(() => BodyDescriptions.StaticColliders2D((GameObject)null, into2D));
        }

        [Test]
        public void TheStaticCollidersUnderARootFollowTheOrderOfTheScene()
        {
            GameObject block = Create("Block");
            var floor = Child(block, "Floor", Vector3.zero).AddComponent<BoxCollider2D>();
            Child(block, "Crate", Vector3.up).AddComponent<Rigidbody2D>().gameObject.AddComponent<CircleCollider2D>();
            var ledge = Child(block, "Ledge", Vector3.right).AddComponent<PolygonCollider2D>();
            ledge.pathCount = 2;
            var wall = Child(block, "Wall", Vector3.left).AddComponent<BoxCollider>();
            var into2D = new List<Collider2D>();
            var into = new List<Collider>();

            BodyDescriptions.StaticColliders2D(block, into2D);
            BodyDescriptions.StaticColliders(block, into);

            CollectionAssert.AreEqual(new Collider2D[] { floor, ledge, ledge }, into2D);
            CollectionAssert.AreEqual(new Collider[] { wall }, into);
        }

        [Test]
        public void TheMotionOfARigidbodyIsCarriedAndAnObjectWithoutOneGetsTheDefault()
        {
            GameObject root = Create("Root");
            root.AddComponent<SphereCollider>();
            var rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rigidbody.useGravity = false;
            rigidbody.linearDamping = 0.5f;
            rigidbody.angularDamping = 0.25f;
            GameObject flat = Create("Flat");
            flat.AddComponent<CircleCollider2D>();
            var rigidbody2D = flat.AddComponent<Rigidbody2D>();
            rigidbody2D.constraints = RigidbodyConstraints2D.FreezeRotation | RigidbodyConstraints2D.FreezePositionX;
            rigidbody2D.gravityScale = 1.5f;
            rigidbody2D.linearDamping = 0.1f;
            rigidbody2D.angularDamping = 0.05f;
            GameObject still = Create("Still");
            still.AddComponent<BoxCollider2D>();

            Assert.IsTrue(BodyDescriptions.TryDescribe(root, out BodyDesc body));
            Assert.IsTrue(BodyDescriptions.TryDescribe2D(flat, out BodyDesc2D body2D));
            Assert.IsTrue(BodyDescriptions.TryDescribe2D(still, out BodyDesc2D staticBody));

            Assert.AreEqual((BodyLocks.PositionY | BodyLocks.RotationX | BodyLocks.RotationZ, false, 0.5f, 0.25f), (body.Motion.Locks, body.Motion.UseGravity, body.Motion.LinearDamping, body.Motion.AngularDamping));
            Assert.AreEqual((BodyLocks2D.Rotation | BodyLocks2D.PositionX, 1.5f, 0.1f, 0.05f), (body2D.Motion.Locks, body2D.Motion.GravityScale, body2D.Motion.LinearDamping, body2D.Motion.AngularDamping));
            Assert.AreEqual((BodyLocks2D.None, 1f, 0f, 0f), (staticBody.Motion.Locks, staticBody.Motion.GravityScale, staticBody.Motion.LinearDamping, staticBody.Motion.AngularDamping));
        }

        [Test]
        public void AnAutoTiledBoxBecomesThePolygonsUnityBuiltForIt()
        {
            Sprite sprite = BorderedSprite();
            foreach (SpriteDrawMode mode in new[] { SpriteDrawMode.Tiled, SpriteDrawMode.Sliced })
            {
                GameObject tile = Create(mode.ToString());
                tile.transform.SetPositionAndRotation(new Vector3(2f, 1f, 0f), Quaternion.Euler(0f, 0f, 30f));
                BoxCollider2D box = AutoTiledBox(tile, sprite, mode, new Vector2(6f, 3f));
                var shapes = new PhysicsShapeGroup2D();
                int count = box.GetShapes(shapes);
                var colliders = new List<ColliderDesc2D>();

                BodyDescriptions.DescribeStatic2D(box, null, colliders);

                Assert.AreEqual(mode == SpriteDrawMode.Tiled ? 8 : 1, count);
                Assert.AreEqual(count, colliders.Count);
                Bounds covered = default;
                for (int shapeIndex = 0; shapeIndex < count; shapeIndex++)
                {
                    ColliderDesc2D collider = colliders[shapeIndex];
                    Assert.AreEqual((ShapeKind2D.ConvexPolygon, 4), (collider.Shape.Kind, collider.Shape.Points.Count));
                    for (int vertex = 0; vertex < 4; vertex++)
                    {
                        System.Numerics.Vector2 point = collider.Shape.Points[vertex];
                        AssertNear(shapes.GetShapeVertex(shapeIndex, vertex), point);
                        var world = new Vector3(point.X, point.Y, 0f);
                        if (shapeIndex == 0 && vertex == 0)
                        {
                            covered = new Bounds(world, Vector3.zero);
                        }

                        covered.Encapsulate(world);
                    }
                }

                Assert.AreEqual(box.bounds.min.x, covered.min.x, 1e-4f);
                Assert.AreEqual(box.bounds.min.y, covered.min.y, 1e-4f);
                Assert.AreEqual(box.bounds.max.x, covered.max.x, 1e-4f);
                Assert.AreEqual(box.bounds.max.y, covered.max.y, 1e-4f);
            }
        }

        [Test]
        public void AnAutoTiledBoxOnARigidbodyIsDescribedInTheSpaceOfItsBody()
        {
            Sprite sprite = BorderedSprite();
            GameObject root = Create("Root");
            root.transform.SetPositionAndRotation(new Vector3(-1f, 4f, 0f), Quaternion.Euler(0f, 0f, 45f));
            root.AddComponent<Rigidbody2D>();
            BoxCollider2D box = AutoTiledBox(root, sprite, SpriteDrawMode.Sliced, new Vector2(4f, 2f));

            Assert.IsTrue(BodyDescriptions.TryDescribe2D(root, out BodyDesc2D body));

            Assert.AreEqual(1, body.Colliders.Count);
            Quaternion turn = Quaternion.AngleAxis(body.Rotation * Mathf.Rad2Deg, Vector3.forward);
            var origin = new Vector3(body.Position.X, body.Position.Y, 0f);
            var shapes = new PhysicsShapeGroup2D();
            box.GetShapes(shapes);
            for (int vertex = 0; vertex < 4; vertex++)
            {
                System.Numerics.Vector2 local = body.Colliders[0].Shape.Points[vertex];
                Vector3 world = origin + turn * new Vector3(local.X, local.Y, 0f);
                Vector2 expected = root.GetComponent<Rigidbody2D>().position + (Vector2)(Quaternion.AngleAxis(root.GetComponent<Rigidbody2D>().rotation, Vector3.forward) * shapes.GetShapeVertex(0, vertex));
                Assert.AreEqual(expected.x, world.x, 1e-4f);
                Assert.AreEqual(expected.y, world.y, 1e-4f);
            }
        }

        private Sprite BorderedSprite()
        {
            var texture = new Texture2D(64, 64);
            created.Add(texture);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f), 32f, 0, SpriteMeshType.FullRect, new Vector4(8f, 8f, 8f, 8f));
            created.Add(sprite);
            return sprite;
        }

        private static BoxCollider2D AutoTiledBox(GameObject holder, Sprite sprite, SpriteDrawMode mode, Vector2 rendererSize)
        {
            var renderer = holder.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.drawMode = mode;
            var box = holder.AddComponent<BoxCollider2D>();
            box.size = new Vector2(1.6f, 1.4f);
            box.offset = new Vector2(0.1f, -0.2f);
            box.autoTiling = true;
            renderer.size = rendererSize;
            Physics2D.SyncTransforms();
            return box;
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
            Assert.AreEqual(expected.x, actual.X, 1e-4f);
            Assert.AreEqual(expected.y, actual.Y, 1e-4f);
        }

        private static void AssertNear(Quaternion expected, System.Numerics.Quaternion actual)
        {
            Assert.AreEqual(1f, Mathf.Abs(Quaternion.Dot(expected, actual.ToUnity())), Tolerance);
        }
    }
}
