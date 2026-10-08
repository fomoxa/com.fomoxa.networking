using System;
using System.Collections.Generic;
using Fomoxa.Networking.Simulation;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity.Tests
{
    public sealed class RuntimeStaticsTest
    {
        private const float StepSeconds = 0.02f;

        private Scene scene;
        private GameObject holder;
        private RigidbodyPhysics physics;

        [SetUp]
        public void CreatePhysics()
        {
            PhysicsSimulationOwner.Acquire();
            scene = EditorSceneManager.NewPreviewScene();
            holder = new GameObject("Physics");
            physics = holder.AddComponent<RigidbodyPhysics>();
        }

        [TearDown]
        public void DestroyPhysics()
        {
            UnityEngine.Object.DestroyImmediate(holder);
            EditorSceneManager.ClosePreviewScene(scene);
            PhysicsSimulationOwner.Release();
        }

        [Test]
        public void AStaticGroupHoldsUpAFallingBodyUntilItIsRemoved()
        {
            StaticGroup group = physics.AddStatic2D(scene, new[] { new ColliderDesc2D(BodyShape2D.Box(new System.Numerics.Vector2(5f, 0.5f)), System.Numerics.Vector2.Zero, 0f, ColliderMaterial.Default, 0, false) });
            PhysicsBody2D ball = physics.AddBody2D(scene, new BodyDesc2D(BodyKind.Dynamic, BodyShape2D.Circle(0.5f), new System.Numerics.Vector2(0f, 2f), 0f, 1f));

            Step2D(100);

            Assert.IsTrue(group.IsValid);
            Assert.AreEqual(1, group.Count);
            Assert.AreEqual(1f, ball.Position.Y, 0.05f);
            GameObject root = Root("FomoxaStatic2D");
            Assert.AreEqual(HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor, root.hideFlags);

            physics.RemoveStatic(group);
            physics.RemoveStatic(group);
            Step2D(50);

            Assert.IsTrue(root == null);
            Assert.Less(ball.Position.Y, 0f);
        }

        [Test]
        public void AnEmptyGroupHasAnIdAndNoObject()
        {
            StaticGroup group = physics.AddStatic2D(scene, Array.Empty<ColliderDesc2D>());

            Assert.IsTrue(group.IsValid);
            Assert.AreEqual(0, group.Count);
            Assert.IsEmpty(scene.GetRootGameObjects());
            Assert.DoesNotThrow(() => physics.RemoveStatic(group));
        }

        [Test]
        public void TheGameObjectOverloadsLeaveTheCollidersOfTheObjectToPhysX()
        {
            var block = new GameObject("Block");
            SceneManager.MoveGameObjectToScene(block, scene);
            block.AddComponent<BoxCollider2D>();

            Assert.AreEqual(default(StaticGroup), physics.AddStatic2D(block));
            Assert.AreEqual(default(StaticGroup), physics.AddStatic(block));
            Assert.AreEqual(1, scene.GetRootGameObjects().Length);
            Assert.Throws<ArgumentNullException>(() => physics.AddStatic2D((GameObject)null));
        }

        [Test]
        public void AStaticMeshGroupDestroysItsMeshWhenRemoved()
        {
            System.Numerics.Vector3[] points = { System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitX, System.Numerics.Vector3.UnitZ };
            StaticGroup group = physics.AddStatic(scene, new[] { new ColliderDesc(BodyShape.TriangleMesh(points, new[] { 0, 1, 2 }), System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, ColliderMaterial.Default, 0, false) });
            Mesh mesh = Root("FomoxaStatic").GetComponentInChildren<MeshCollider>().sharedMesh;

            physics.RemoveStatic(group);

            Assert.IsTrue(mesh == null);
        }

        [Test]
        public void UnownedBodiesLiveInTheSceneUntilRemovedAndDynamicOnesAreRewound()
        {
            PhysicsBody2D mover = physics.AddBody2D(scene, new BodyDesc2D(BodyKind.Kinematic, BodyShape2D.Box(System.Numerics.Vector2.One), System.Numerics.Vector2.Zero, 0f, 0f));
            PhysicsBody2D falling = physics.AddBody2D(scene, new BodyDesc2D(BodyKind.Dynamic, BodyShape2D.Circle(0.5f), new System.Numerics.Vector2(10f, 0f), 0f, 1f));
            IPhysicsSimulation world = World2D();
            PhysicsSnapshot snapshot = world.CreateSnapshot();

            mover.Position = new System.Numerics.Vector2(3f, 0f);
            world.Save(snapshot);
            float savedHeight = falling.Position.Y;
            Step2D(10);
            world.Load(snapshot);

            Assert.AreEqual(3f, mover.Position.X, 1e-4f);
            Assert.AreEqual(savedHeight, falling.Position.Y, 1e-4f);
            Assert.AreEqual(2, scene.GetRootGameObjects().Length);

            physics.RemoveBody2D(mover);
            physics.RemoveBody2D(falling);

            Assert.IsFalse(mover.IsValid);
            Assert.IsFalse(falling.IsValid);
            Assert.IsEmpty(scene.GetRootGameObjects());
        }

        private IPhysicsSimulation World2D()
        {
            var worlds = new List<IPhysicsSimulation>();
            physics.WorldsOf(scene, worlds);
            return worlds.Find(world => world is UnityPhysicsWorld2D);
        }

        private void Step2D(int steps)
        {
            IPhysicsSimulation world = World2D();
            for (int step = 0; step < steps; step++)
            {
                world.Step(StepSeconds);
            }
        }

        private GameObject Root(string name)
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
