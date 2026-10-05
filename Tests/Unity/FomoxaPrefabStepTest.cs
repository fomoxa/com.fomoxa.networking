using System.Collections.Generic;
using System.Linq;
using Fomoxa.Unity.Editor;
using Fomoxa.Unity.Tests.Support;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity.Tests
{
    public sealed class FomoxaPrefabStepTest
    {
        private const string Folder = "Assets/FomoxaPrefabStepTest";

        [SetUp]
        public void CreateFolder()
        {
            AssetDatabase.CreateFolder("Assets", "FomoxaPrefabStepTest");
        }

        [TearDown]
        public void DeleteFolder()
        {
            AssetDatabase.DeleteAsset(Folder);
            foreach (NetworkManager manager in Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Object.DestroyImmediate(manager.gameObject);
            }

            TestPrefabs.DestroyAllInScene();
        }

        [Test]
        public void PrefabIdIsTheFnv1aOfTheAssetGuid()
        {
            Assert.AreEqual(0x86D04DB5u, FomoxaPrefabStep.PrefabIdOf("0123456789abcdef0123456789abcdef"));
        }

        [Test]
        public void ConstantsNameEachPrefabAndQualifyRepeatedNames()
        {
            var prefabs = new[]
            {
                new FomoxaPrefab("Assets/2 Big-Tree.prefab", null, 0x00000004, 0x0000000D),
                new FomoxaPrefab("Assets/A/Player.prefab", null, 0x00000002, 0x0000000B),
                new FomoxaPrefab("Assets/B/Player.prefab", null, 0x00000003, 0x0000000C),
                new FomoxaPrefab("Assets/Enemies/Goblin.prefab", null, 0x1234ABCD, 0x0000000A),
            };

            string text = FomoxaPrefabStep.Constants("Generated", prefabs);

            Assert.AreEqual(
                "namespace Generated\n"
                + "{\n"
                + "    public static class FomoxaPrefabs\n"
                + "    {\n"
                + "        public const uint _2_Big_TreeId = 0x00000004;\n"
                + "        public const uint _2_Big_TreeFingerprint = 0x0000000D;\n"
                + "\n"
                + "        public const uint A_PlayerId = 0x00000002;\n"
                + "        public const uint A_PlayerFingerprint = 0x0000000B;\n"
                + "\n"
                + "        public const uint B_PlayerId = 0x00000003;\n"
                + "        public const uint B_PlayerFingerprint = 0x0000000C;\n"
                + "\n"
                + "        public const uint GoblinId = 0x1234ABCD;\n"
                + "        public const uint GoblinFingerprint = 0x0000000A;\n"
                + "    }\n"
                + "}\n",
                text);
        }

        [Test]
        public void AssignAndValidateGivesEachPrefabItsGuidId()
        {
            string plainPath = Save(TestPrefabs.Create("Plain", 0), "Plain");
            string explicitPath = Save(TestPrefabs.Create("Explicit", 0x55, true), "Explicit");
            Save(new GameObject("NotNetworked"), "NotNetworked");
            var valid = new List<FomoxaPrefab>();

            List<string> errors = FomoxaPrefabStep.AssignAndValidate(new[] { Folder }, valid);

            Assert.IsEmpty(errors);
            Assert.AreEqual(new[] { explicitPath, plainPath }, valid.Select(prefab => prefab.Path).ToArray());
            uint plainId = FomoxaPrefabStep.PrefabIdOf(AssetDatabase.AssetPathToGUID(plainPath));
            Assert.AreEqual(plainId, LoadNetworkObject(plainPath).PrefabId);
            Assert.AreEqual(plainId, valid[1].PrefabId);
            Assert.AreEqual(0x62D73B48u, valid[1].Fingerprint);
            Assert.AreEqual(0x55u, LoadNetworkObject(explicitPath).PrefabId);
        }

        [Test]
        public void AssignAndValidateReportsEveryInvalidPrefab()
        {
            string firstPath = Save(TestPrefabs.Create("First", 0x77, true), "First");
            string secondPath = Save(TestPrefabs.Create("Second", 0x77, true), "Second");
            string zeroPath = Save(TestPrefabs.Create("Zero", 0, true), "Zero");
            NetworkObject nested = TestPrefabs.Create("Nested", 0);
            TestPrefabs.Create("Inner", 0).transform.SetParent(nested.transform);
            string nestedPath = Save(nested, "Nested");
            var offRoot = new GameObject("OffRoot");
            TestPrefabs.Create("Child", 0).transform.SetParent(offRoot.transform);
            string offRootPath = Save(offRoot, "OffRoot");
            var valid = new List<FomoxaPrefab>();

            List<string> errors = FomoxaPrefabStep.AssignAndValidate(new[] { Folder }, valid);

            Assert.AreEqual(new[] { firstPath }, valid.Select(prefab => prefab.Path).ToArray());
            Assert.AreEqual(4, errors.Count);
            Assert.IsTrue(errors.Any(error => error.StartsWith(secondPath) && error.Contains("0x00000077") && error.Contains(firstPath)));
            Assert.IsTrue(errors.Any(error => error.StartsWith(zeroPath) && error.Contains("prefab id is 0")));
            Assert.IsTrue(errors.Any(error => error.StartsWith(nestedPath) && error.Contains("nested")));
            Assert.IsTrue(errors.Any(error => error.StartsWith(offRootPath) && error.Contains("root")));
        }

        [Test]
        public void PrefabAssetLosesItsSceneObjectId()
        {
            string path = Save(TestPrefabs.CreateSceneObject("FromScene", 0x12345678_00000001, 0x66), "FromScene");
            var valid = new List<FomoxaPrefab>();

            FomoxaPrefabStep.AssignAndValidate(new[] { Folder }, valid);

            Assert.AreEqual(0UL, LoadNetworkObject(path).SceneObjectId);
        }

        [Test]
        public void WriteListCreatesThenUpdatesTheAsset()
        {
            NetworkObject first = LoadNetworkObject(Save(TestPrefabs.Create("First", 0xA1, true), "First"));
            NetworkObject second = LoadNetworkObject(Save(TestPrefabs.Create("Second", 0xA2, true), "Second"));
            string listPath = Folder + "/" + FomoxaPrefabStep.ListFileName;

            FomoxaPrefabStep.WriteList(listPath, new[] { first });
            Assert.AreEqual(new[] { first }, AssetDatabase.LoadAssetAtPath<NetworkPrefabList>(listPath).Prefabs.ToArray());

            FomoxaPrefabStep.WriteList(listPath, new[] { first, second });
            Assert.AreEqual(new[] { first, second }, AssetDatabase.LoadAssetAtPath<NetworkPrefabList>(listPath).Prefabs.ToArray());
        }

        [Test]
        public void AssignCollectedSetsTheListOnlyWhereCollectionIsOn()
        {
            var list = ScriptableObject.CreateInstance<NetworkPrefabList>();
            var collecting = new GameObject("Collecting").AddComponent<NetworkManager>();
            var manual = new GameObject("Manual").AddComponent<NetworkManager>();
            var serialized = new SerializedObject(manual);
            serialized.FindProperty("collectPrefabs").boolValue = false;
            serialized.FindProperty("collectedPrefabs").objectReferenceValue = list;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            FomoxaPrefabStep.AssignCollected(SceneManager.GetActiveScene(), list);

            Assert.AreSame(list, collecting.CollectedPrefabs);
            Assert.IsNull(manual.CollectedPrefabs);
            Object.DestroyImmediate(list);
        }

        private static NetworkObject LoadNetworkObject(string path) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<NetworkObject>();

        private static string Save(NetworkObject networkObject, string name) => Save(networkObject.gameObject, name);

        private static string Save(GameObject gameObject, string name)
        {
            string path = Folder + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(gameObject, path);
            Object.DestroyImmediate(gameObject);
            return path;
        }
    }
}
