using System.Collections.Generic;
using System.Linq;
using Fomoxa.Unity.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity.Tests
{
    public sealed class FomoxaSceneStepTest
    {
        private const string Folder = "Assets/FomoxaSceneStepTest";

        [SetUp]
        public void CreateFolder()
        {
            AssetDatabase.CreateFolder("Assets", "FomoxaSceneStepTest");
        }

        [TearDown]
        public void DeleteFolder()
        {
            AssetDatabase.DeleteAsset(Folder);
            foreach (NetworkManager manager in Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Object.DestroyImmediate(manager.gameObject);
            }
        }

        [Test]
        public void ConstantsNameEveryScene()
        {
            var scenes = new[]
            {
                new FomoxaScene("Assets/Scenes/Arena.unity", 0x0000ABCD),
                new FomoxaScene("Assets/A/Room.unity", 0x00000002),
                new FomoxaScene("Assets/B/Room.unity", 0x00000003),
            };

            string text = FomoxaSceneStep.Constants("Generated", scenes);

            Assert.AreEqual(
                "namespace Generated\n"
                + "{\n"
                + "    public static class FomoxaScenes\n"
                + "    {\n"
                + "        public const uint ArenaId = 0x0000ABCD;\n"
                + "        public const uint A_RoomId = 0x00000002;\n"
                + "        public const uint B_RoomId = 0x00000003;\n"
                + "    }\n"
                + "}\n",
                text);
        }

        [Test]
        public void ValidateRefusesIdZeroAndRepeatedIds()
        {
            var valid = new List<FomoxaScene>();

            List<string> errors = FomoxaSceneStep.Validate(
                new[]
                {
                    new FomoxaScene("Assets/Arena.unity", 0x10),
                    new FomoxaScene("Assets/Zero.unity", 0),
                    new FomoxaScene("Assets/Clash.unity", 0x10),
                    new FomoxaScene("Assets/Lobby.unity", 0x20),
                },
                valid);

            CollectionAssert.AreEqual(new[] { "Assets/Arena.unity", "Assets/Lobby.unity" }, valid.Select(scene => scene.Path));
            Assert.AreEqual(2, errors.Count);
            StringAssert.Contains("Assets/Zero.unity", errors[0]);
            StringAssert.Contains("Assets/Clash.unity", errors[1]);
            StringAssert.Contains("Assets/Arena.unity", errors[1]);
        }

        [Test]
        public void ValidateGivesEachSceneAssetTheHashOfItsGuid()
        {
            string path = Folder + "/Arena.unity";
            System.IO.File.WriteAllText(path, "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(path);
            var valid = new List<FomoxaScene>();

            List<string> errors = FomoxaSceneStep.Validate(new[] { Folder }, valid);

            Assert.AreEqual(0, errors.Count);
            Assert.AreEqual(1, valid.Count);
            Assert.AreEqual(path, valid[0].Path);
            Assert.AreEqual(FomoxaSceneObjectIds.SceneHashOf(AssetDatabase.AssetPathToGUID(path)), valid[0].SceneId);
        }

        [Test]
        public void TheListHoldsOnlyEnabledBuildScenesInBuildOrder()
        {
            var valid = new[]
            {
                new FomoxaScene("Assets/Arena.unity", 0x10),
                new FomoxaScene("Assets/Lobby.unity", 0x20),
                new FomoxaScene("Assets/Room.unity", 0x30),
            };
            var build = new[]
            {
                new EditorBuildSettingsScene("Assets/Room.unity", true),
                new EditorBuildSettingsScene("Assets/Lobby.unity", false),
                new EditorBuildSettingsScene("Assets/Other.unity", true),
                new EditorBuildSettingsScene("Assets/Arena.unity", true),
            };

            List<FomoxaScene> scenes = FomoxaSceneStep.BuildScenes(valid, build);

            CollectionAssert.AreEqual(new[] { "Assets/Room.unity", "Assets/Arena.unity" }, scenes.Select(scene => scene.Path));
        }

        [Test]
        public void WriteListCreatesThenUpdatesTheAsset()
        {
            string listPath = Folder + "/" + FomoxaSceneStep.ListFileName;

            FomoxaSceneStep.WriteList(listPath, new[] { new FomoxaScene("Assets/Arena.unity", 0x10) });
            var list = AssetDatabase.LoadAssetAtPath<NetworkSceneList>(listPath);
            Assert.AreEqual(1, list.Count);

            FomoxaSceneStep.WriteList(listPath, new[] { new FomoxaScene("Assets/Arena.unity", 0x10), new FomoxaScene("Assets/Lobby.unity", 0x20) });
            list = AssetDatabase.LoadAssetAtPath<NetworkSceneList>(listPath);
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual(0x20u, list.SceneIdAt(1));
            Assert.AreEqual("Assets/Lobby.unity", list.PathAt(1));
        }

        [Test]
        public void AssignCollectedSetsTheListOnEveryNetworkManager()
        {
            var list = ScriptableObject.CreateInstance<NetworkSceneList>();
            var manager = new GameObject("Manager").AddComponent<NetworkManager>();

            FomoxaSceneStep.AssignCollected(SceneManager.GetActiveScene(), list);

            Assert.AreSame(list, manager.CollectedScenes);
            Object.DestroyImmediate(list);
        }
    }
}
