using System;
using System.Collections.Generic;
using System.IO;
using Fomoxa.Networking;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Simulation;
using Fomoxa.Unity.Editor;
using Fomoxa.Unity.Tests.Support;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity.Tests
{
    public sealed class FomoxaSceneFileStepTest
    {
        private const string AssetFolder = "Assets/FomoxaSceneFileStepTest";

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
            AssetDatabase.DeleteAsset(AssetFolder);
        }

        [Test]
        public void SceneObjectsTheirBodiesStaticCollidersAndLayerMatricesAreDescribed()
        {
            GameObject wall = Create("Wall");
            wall.transform.position = new Vector3(0f, 1f, 0f);
            wall.layer = 4;
            wall.AddComponent<BoxCollider>().size = new Vector3(2f, 2f, 2f);
            GameObject floor = Create("Floor");
            floor.AddComponent<BoxCollider2D>().size = new Vector2(4f, 1f);
            GameObject cosmetic = Create("Cosmetic");
            cosmetic.AddComponent<Rigidbody>();
            cosmetic.AddComponent<SphereCollider>();
            GameObject door = Create("Door");
            door.transform.position = new Vector3(3f, 0f, 0f);
            var networkObject = door.AddComponent<NetworkObject>();
            networkObject.SetSceneObjectId(0x0000_0001_0000_0002);
            door.AddComponent<RpcBehaviour>();
            var doorBody = door.AddComponent<Rigidbody2D>();
            doorBody.mass = 2f;
            doorBody.constraints = RigidbodyConstraints2D.FreezeRotation;
            doorBody.gravityScale = 0.5f;
            doorBody.linearDamping = 0.2f;
            door.AddComponent<CircleCollider2D>().radius = 0.5f;
            var errors = new List<string>();

            SceneFile file = FomoxaSceneFileStep.Describe(scene, 0x99, errors);

            CollectionAssert.IsEmpty(errors);
            Assert.AreEqual(0x99u, file.SceneId);
            Assert.AreEqual(1, file.Objects.Count);
            SceneFileObject entry = file.Objects[0];
            Assert.AreEqual(0x0000_0001_0000_0002UL, entry.SceneObjectId);
            CollectionAssert.AreEqual(new[] { typeof(RpcBehaviour).FullName }, entry.BehaviourTypes);
            Assert.AreEqual(3f, entry.Pose.PositionX);
            Assert.IsFalse(SceneFileGeometry.TryGetBody(entry, out _));
            Assert.IsTrue(SceneFileGeometry.TryGetBody2D(entry, out BodyDesc2D body));
            Assert.AreEqual((BodyKind.Dynamic, 2f, ShapeKind2D.Circle), (body.Kind, body.Mass, body.Colliders[0].Shape.Kind));
            Assert.AreEqual((BodyLocks2D.Rotation, 0.5f, 0.2f), (body.Motion.Locks, body.Motion.GravityScale, body.Motion.LinearDamping));
            Assert.AreEqual(1, file.Colliders.Count);
            ColliderDesc box = SceneFileGeometry.ToDesc(file.Colliders[0]);
            Assert.AreEqual((ShapeKind.Box, new System.Numerics.Vector3(1f, 1f, 1f), new System.Numerics.Vector3(0f, 1f, 0f), 4), (box.Shape.Kind, box.Shape.HalfExtents, box.Position, box.Layer));
            Assert.AreEqual(1, file.Colliders2D.Count);
            Assert.AreEqual(ShapeKind2D.Box, SceneFileGeometry.ToDesc(file.Colliders2D[0]).Shape.Kind);
            Assert.AreEqual(32, file.LayerCollisions.Count);
            Assert.AreEqual(32, file.LayerCollisions2D.Count);
            for (int layer = 0; layer < 32; layer++)
            {
                for (int other = 0; other < 32; other++)
                {
                    Assert.AreEqual(!Physics.GetIgnoreLayerCollision(layer, other), (file.LayerCollisions[layer] & (1u << other)) != 0);
                    Assert.AreEqual(!Physics2D.GetIgnoreLayerCollision(layer, other), (file.LayerCollisions2D[layer] & (1u << other)) != 0);
                }
            }
        }

        [Test]
        public void StaticCollidersAreWrittenInTheOrderOfBodyDescriptions()
        {
            Create("Left").AddComponent<BoxCollider>().center = new Vector3(-1f, 0f, 0f);
            Create("Right").AddComponent<SphereCollider>().center = new Vector3(1f, 0f, 0f);
            var ground = Create("Ground").AddComponent<PolygonCollider2D>();
            ground.pathCount = 2;
            ground.SetPath(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f) });
            ground.SetPath(1, new[] { new Vector2(5f, 0f), new Vector2(6f, 0f), new Vector2(6f, 1f), new Vector2(5f, 1f) });
            Create("Coin").AddComponent<CircleCollider2D>();
            var sources = new List<Collider>();
            var sources2D = new List<Collider2D>();
            var errors = new List<string>();

            SceneFile file = FomoxaSceneFileStep.Describe(scene, 0x99, errors);
            BodyDescriptions.StaticColliders(scene, sources);
            BodyDescriptions.StaticColliders2D(scene, sources2D);

            CollectionAssert.IsEmpty(errors);
            Assert.AreEqual(sources.Count, file.Colliders.Count);
            Assert.AreEqual((ShapeKind.Box, ShapeKind.Sphere), (SceneFileGeometry.ToDesc(file.Colliders[0]).Shape.Kind, SceneFileGeometry.ToDesc(file.Colliders[1]).Shape.Kind));
            Assert.AreEqual(sources2D.Count, file.Colliders2D.Count);
            Assert.AreEqual(4, SceneFileGeometry.ToDesc(file.Colliders2D[0]).Shape.Points.Count);
            Assert.AreEqual(5, SceneFileGeometry.ToDesc(file.Colliders2D[1]).Shape.Points.Count);
            Assert.AreEqual(ShapeKind2D.Circle, SceneFileGeometry.ToDesc(file.Colliders2D[2]).Shape.Kind);
        }

        [Test]
        public void ATerrainBecomesATriangleMeshWithoutItsHoles()
        {
            var data = new TerrainData { heightmapResolution = 33, size = new Vector3(32f, 10f, 32f) };
            created.Add(data);
            var heights = new float[33, 33];
            for (int z = 0; z < 33; z++)
            {
                for (int x = 0; x < 33; x++)
                {
                    heights[z, x] = 0.5f;
                }
            }

            data.SetHeights(0, 0, heights);
            data.SetHoles(0, 0, new bool[1, 1] { { false } });
            GameObject ground = Create("Ground");
            ground.transform.position = new Vector3(10f, 0f, 0f);
            ground.AddComponent<TerrainCollider>().terrainData = data;
            var errors = new List<string>();

            SceneFile file = FomoxaSceneFileStep.Describe(scene, 0x99, errors);

            CollectionAssert.IsEmpty(errors);
            ColliderDesc terrain = SceneFileGeometry.ToDesc(file.Colliders[0]);
            Assert.AreEqual((ShapeKind.TriangleMesh, 33 * 33, (32 * 32 - 1) * 6), (terrain.Shape.Kind, terrain.Shape.Points.Count, terrain.Shape.Triangles.Count));
            Assert.AreEqual(new System.Numerics.Vector3(10f, 0f, 0f), terrain.Position);
            Assert.AreEqual(new System.Numerics.Vector3(1f, 5f, 0f), terrain.Shape.Points[1]);
        }

        [Test]
        public void ErrorsLeaveTheSceneWithoutAFile()
        {
            GameObject unnamed = Create("Unnamed");
            unnamed.AddComponent<NetworkObject>();
            GameObject rounded = Create("Rounded");
            rounded.AddComponent<BoxCollider2D>().edgeRadius = 0.25f;
            var errors = new List<string>();

            Assert.IsNull(FomoxaSceneFileStep.Describe(scene, 0x99, errors));

            Assert.AreEqual(2, errors.Count);
            StringAssert.Contains("Unnamed has no scene object id", errors[0]);
            StringAssert.Contains("edgeRadius", errors[1]);
        }

        [Test]
        public void AFileIsWrittenOnlyWhenItsBytesChange()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "1" + SceneFileFormat.Extension);
            try
            {
                Assert.IsTrue(FomoxaSceneFileStep.WriteIfChanged(path, new byte[] { 1, 2 }));
                Assert.IsFalse(FomoxaSceneFileStep.WriteIfChanged(path, new byte[] { 1, 2 }));
                Assert.IsTrue(FomoxaSceneFileStep.WriteIfChanged(path, new byte[] { 1, 3 }));
                CollectionAssert.AreEqual(new byte[] { 1, 3 }, File.ReadAllBytes(path));
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(path), true);
            }
        }

        [Test]
        public void ASceneFileIsImportedAsATextAssetThatTheSceneListCarriesIntoTheRegistry()
        {
            byte[] bytes = { 7, 0, 1, 255 };
            string assetPath = FomoxaSceneFileStep.FilePath(AssetFolder, 123);
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            FomoxaSceneFileStep.WriteIfChanged(Path.Combine(projectRoot, assetPath), bytes);
            AssetDatabase.ImportAsset(assetPath);

            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            var list = ScriptableObject.CreateInstance<NetworkSceneList>();
            created.Add(list);
            list.Set(new[] { (123u, "Assets/Scenes/Arena.unity", asset), (124u, "Assets/Scenes/Lobby.unity", (TextAsset)null) });
            var registry = new SceneRegistry();
            registry.RegisterBuildScenes(list);

            Assert.IsNotNull(asset);
            CollectionAssert.AreEqual(bytes, asset.bytes);
            Assert.IsTrue(registry.TryGetSceneFile(123, out byte[] found));
            CollectionAssert.AreEqual(bytes, found);
            Assert.IsFalse(registry.TryGetSceneFile(124, out _));
            Assert.IsFalse(registry.TryGetSceneFile(125, out _));
        }

        [Test]
        public void TheApplicationRegistryWritesAndReadsTheDescribedScene()
        {
            Create("Wall").AddComponent<BoxCollider>();

            FomoxaRegistry registry = FomoxaSceneFileStep.ApplicationRegistry(out string error);
            SceneFile file = FomoxaSceneFileStep.Describe(scene, 0x42, new List<string>());

            Assert.IsNotNull(registry, error);
            SceneFile read = SceneFileFormat.Read(registry, SceneFileFormat.Write(registry, file));
            Assert.AreEqual((0x42u, 1, 32), (read.SceneId, read.Colliders.Count, read.LayerCollisions.Count));
        }

        private GameObject Create(string name)
        {
            var gameObject = new GameObject(name);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            created.Add(gameObject);
            return gameObject;
        }
    }
}
