using System;
using Fomoxa.Networking.Simulation;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity.Tests
{
    public sealed class CreatedBodyCollidersTest
    {
        private Scene scene;
        private UnityPhysicsWorld world;
        private UnityPhysicsWorld2D world2D;

        [SetUp]
        public void CreateWorlds()
        {
            scene = EditorSceneManager.NewPreviewScene();
            world = new UnityPhysicsWorld(scene);
            world2D = new UnityPhysicsWorld2D(scene);
        }

        [TearDown]
        public void CloseScene() => EditorSceneManager.ClosePreviewScene(scene);

        [Test]
        public void EachColliderIsAChildWithItsPoseLayerTriggerAndMaterial()
        {
            var material = new ColliderMaterial(0.2f, 0.5f, CombineRule.Maximum, CombineRule.Minimum);
            ColliderDesc[] colliders =
            {
                new ColliderDesc(BodyShape.Box(new System.Numerics.Vector3(1f, 2f, 3f)), new System.Numerics.Vector3(0f, 1f, 0f), System.Numerics.Quaternion.Identity, material, 3, true),
                new ColliderDesc(BodyShape.Sphere(0.5f), System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, ColliderMaterial.Default, 0, false),
            };

            BodyHandle handle = world.CreateBody(new BodyDesc(BodyKind.Dynamic, colliders, System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, 2f));

            GameObject body = CreatedBody("FomoxaBody");
            Assert.AreEqual(2, body.transform.childCount);
            Transform first = body.transform.GetChild(0);
            var box = first.GetComponent<BoxCollider>();
            Assert.AreEqual(new Vector3(2f, 4f, 6f), box.size);
            Assert.AreEqual(new Vector3(0f, 1f, 0f), first.localPosition);
            Assert.AreEqual((3, true), (first.gameObject.layer, box.isTrigger));
            Assert.AreEqual((0.2f, 0.2f, 0.5f, PhysicsMaterialCombine.Maximum, PhysicsMaterialCombine.Minimum), (box.sharedMaterial.dynamicFriction, box.sharedMaterial.staticFriction, box.sharedMaterial.bounciness, box.sharedMaterial.frictionCombine, box.sharedMaterial.bounceCombine));
            Assert.AreEqual(0.5f, body.transform.GetChild(1).GetComponent<SphereCollider>().radius);
            Assert.AreSame(body.GetComponent<Rigidbody>(), box.attachedRigidbody);

            Assert.IsTrue(world.RemoveBody(handle));
            Assert.IsTrue(body == null);
        }

        [Test]
        public void HullsAndStaticMeshesBecomeMeshCollidersThatAreDestroyedWithTheBody()
        {
            System.Numerics.Vector3[] points = { System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitX, System.Numerics.Vector3.UnitY, System.Numerics.Vector3.UnitZ };
            BodyHandle rock = world.CreateBody(new BodyDesc(BodyKind.Dynamic, BodyShape.ConvexHull(points), System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, 1f));
            GameObject rockBody = CreatedBody("FomoxaBody");
            rockBody.name = "Rock";
            BodyHandle ground = world.CreateBody(new BodyDesc(BodyKind.Static, BodyShape.TriangleMesh(points, new[] { 0, 1, 2, 0, 2, 3 }), System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, 0f));
            GameObject groundBody = CreatedBody("FomoxaBody");

            var hull = rockBody.GetComponentInChildren<MeshCollider>();
            var mesh = groundBody.GetComponentInChildren<MeshCollider>();
            Assert.AreEqual((true, 4), (hull.convex, hull.sharedMesh.vertexCount));
            Assert.AreEqual((false, 6), (mesh.convex, mesh.sharedMesh.triangles.Length));
            Mesh hullMesh = hull.sharedMesh;
            Mesh groundMesh = mesh.sharedMesh;

            world.RemoveBody(rock);
            world.RemoveBody(ground);

            Assert.IsTrue(hullMesh == null);
            Assert.IsTrue(groundMesh == null);
        }

        [Test]
        public void TheMeanRuleIsRefusedIn3DBeforeAnythingIsCreated()
        {
            var collider = new ColliderDesc(BodyShape.Sphere(1f), System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, new ColliderMaterial(0.4f, 0f, CombineRule.Mean, CombineRule.Average), 0, false);

            Assert.Throws<NotSupportedException>(() => world.CreateBody(new BodyDesc(BodyKind.Dynamic, new[] { collider }, System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, 1f)));
            Assert.AreEqual(0, scene.GetRootGameObjects().Length);
        }

        [Test]
        public void TwoDimensionalPolygonsAndPolylinesBecomePolygonAndEdgeColliders()
        {
            System.Numerics.Vector2[] triangle = { System.Numerics.Vector2.Zero, System.Numerics.Vector2.UnitX, System.Numerics.Vector2.UnitY };
            var material = new ColliderMaterial(0.3f, 0.2f, CombineRule.Mean, CombineRule.Maximum);
            ColliderDesc2D[] colliders =
            {
                new ColliderDesc2D(BodyShape2D.ConvexPolygon(triangle), new System.Numerics.Vector2(1f, 0f), Mathf.PI * 0.5f, material, 4, false),
                new ColliderDesc2D(BodyShape2D.Capsule(0.5f, 1f), System.Numerics.Vector2.Zero, 0f, ColliderMaterial.Default, 0, true),
            };
            world2D.CreateBody(new BodyDesc2D(BodyKind.Dynamic, colliders, System.Numerics.Vector2.Zero, 0f, 1f));
            world2D.CreateBody(new BodyDesc2D(BodyKind.Static, BodyShape2D.Polyline(triangle), System.Numerics.Vector2.Zero, 0f, 0f));

            GameObject[] bodies = scene.GetRootGameObjects();
            Transform polygonPart = bodies[0].transform.GetChild(0);
            var polygon = polygonPart.GetComponent<PolygonCollider2D>();
            Assert.AreEqual((1, 3, 4), (polygon.pathCount, polygon.GetPath(0).Length, polygonPart.gameObject.layer));
            Assert.AreEqual(90f, polygonPart.localEulerAngles.z, 1e-3f);
            Assert.AreEqual((0.3f, 0.2f, PhysicsMaterialCombine2D.Mean, PhysicsMaterialCombine2D.Maximum), (polygon.sharedMaterial.friction, polygon.sharedMaterial.bounciness, polygon.sharedMaterial.frictionCombine, polygon.sharedMaterial.bounceCombine));
            var capsule = bodies[0].transform.GetChild(1).GetComponent<CapsuleCollider2D>();
            Assert.AreEqual((new Vector2(1f, 3f), true), (capsule.size, capsule.isTrigger));
            Assert.AreEqual(3, bodies[1].GetComponentInChildren<EdgeCollider2D>().pointCount);
        }

        [Test]
        public void ACreatedBodyTakesTheMotionOfItsDescription()
        {
            ColliderDesc[] sphere = { new ColliderDesc(BodyShape.Sphere(0.5f), System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, ColliderMaterial.Default, 0, false) };
            ColliderDesc2D[] circle = { new ColliderDesc2D(BodyShape2D.Circle(0.5f), System.Numerics.Vector2.Zero, 0f, ColliderMaterial.Default, 0, false) };

            world.CreateBody(new BodyDesc(BodyKind.Dynamic, sphere, System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, 1f, new BodyMotion(BodyLocks.PositionY | BodyLocks.RotationZ, false, 0.5f, 0.25f)));
            world2D.CreateBody(new BodyDesc2D(BodyKind.Dynamic, circle, System.Numerics.Vector2.Zero, 0f, 1f, new BodyMotion2D(BodyLocks2D.Rotation, 1.5f, 0.1f, 0.05f)));

            var rigidbody = CreatedBody("FomoxaBody").GetComponent<Rigidbody>();
            var rigidbody2D = CreatedBody("FomoxaBody2D").GetComponent<Rigidbody2D>();
            Assert.AreEqual((RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationZ, false, 0.5f, 0.25f), (rigidbody.constraints, rigidbody.useGravity, rigidbody.linearDamping, rigidbody.angularDamping));
            Assert.AreEqual((RigidbodyConstraints2D.FreezeRotation, 1.5f, 0.1f, 0.05f), (rigidbody2D.constraints, rigidbody2D.gravityScale, rigidbody2D.linearDamping, rigidbody2D.angularDamping));
        }

        private GameObject CreatedBody(string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                {
                    return root;
                }
            }

            Assert.Fail($"no {name} in the scene");
            return null;
        }
    }
}
