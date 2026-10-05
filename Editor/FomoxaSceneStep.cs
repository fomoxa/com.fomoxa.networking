using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity.Editor
{
    public static class FomoxaSceneStep
    {
        public const string ConstantsFileName = "FomoxaScenes.cs";
        public const string ListFileName = "FomoxaSceneList.asset";

        private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false);

        [MenuItem("Tools/Fomoxa/Collect Network Scenes")]
        public static void RunFromMenu()
        {
            RunAndLog();
        }

        public static void RunAndLog()
        {
            foreach (string error in RunForProject())
            {
                Debug.LogError("[Fomoxa] " + error);
            }
        }

        public static IReadOnlyList<string> RunForProject()
        {
            var scenes = new List<FomoxaScene>();
            List<string> errors = Validate(new[] { "Assets" }, scenes);
            FomoxaSettings settings = FomoxaSettings.Find(out string _);
            if (settings == null || string.IsNullOrWhiteSpace(settings.GeneratedFolder))
            {
                return errors;
            }

            string generatedFolder = settings.GeneratedFolder.Trim().TrimEnd('/');
            WriteList(generatedFolder + "/" + ListFileName, BuildScenes(scenes, EditorBuildSettings.scenes));
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string namespaceName = FomoxaGenerator.NamespaceOf(Path.Combine(projectRoot, generatedFolder));
            if (namespaceName != null)
            {
                string constantsPath = generatedFolder + "/" + ConstantsFileName;
                if (WriteIfChanged(Path.Combine(projectRoot, constantsPath), Constants(namespaceName, scenes)))
                {
                    AssetDatabase.ImportAsset(constantsPath);
                }
            }

            return errors;
        }

        public static bool OutputsExist()
        {
            FomoxaSettings settings = FomoxaSettings.Find(out string _);
            if (settings == null || string.IsNullOrWhiteSpace(settings.GeneratedFolder))
            {
                return true;
            }

            string generatedFolder = settings.GeneratedFolder.Trim().TrimEnd('/');
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return File.Exists(Path.Combine(projectRoot, generatedFolder, ListFileName))
                && File.Exists(Path.Combine(projectRoot, generatedFolder, ConstantsFileName));
        }

        public static List<string> Validate(string[] folders, List<FomoxaScene> valid)
        {
            IEnumerable<string> paths = AssetDatabase.FindAssets("t:Scene", folders)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .OrderBy(path => path, StringComparer.Ordinal);
            return Validate(paths.Select(path => new FomoxaScene(path, FomoxaSceneObjectIds.SceneHashOf(AssetDatabase.AssetPathToGUID(path)))), valid);
        }

        public static List<string> Validate(IEnumerable<FomoxaScene> scenes, List<FomoxaScene> valid)
        {
            var errors = new List<string>();
            var owners = new Dictionary<uint, string>();
            foreach (FomoxaScene scene in scenes)
            {
                if (scene.SceneId == 0)
                {
                    errors.Add($"{scene.Path}: the scene id is 0; recreate the scene asset to give it a new GUID");
                    continue;
                }

                if (owners.TryGetValue(scene.SceneId, out string owner))
                {
                    errors.Add($"{scene.Path}: scene id 0x{scene.SceneId:X8} is already used by {owner}; recreate one of the scene assets to give it a new GUID");
                    continue;
                }

                owners.Add(scene.SceneId, scene.Path);
                valid.Add(scene);
            }

            return errors;
        }

        public static List<FomoxaScene> BuildScenes(IReadOnlyList<FomoxaScene> valid, IEnumerable<EditorBuildSettingsScene> buildScenes)
        {
            var byPath = valid.ToDictionary(scene => scene.Path, StringComparer.Ordinal);
            var result = new List<FomoxaScene>();
            foreach (EditorBuildSettingsScene buildScene in buildScenes)
            {
                if (buildScene.enabled && byPath.TryGetValue(buildScene.path, out FomoxaScene scene))
                {
                    result.Add(scene);
                }
            }

            return result;
        }

        public static void WriteList(string assetPath, IReadOnlyList<FomoxaScene> scenes)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkSceneList>(assetPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkSceneList>();
                list.Set(scenes.Select(scene => (scene.SceneId, scene.Path)));
                AssetDatabase.CreateAsset(list, assetPath);
                return;
            }

            if (Matches(list, scenes))
            {
                return;
            }

            list.Set(scenes.Select(scene => (scene.SceneId, scene.Path)));
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssetIfDirty(list);
        }

        public static NetworkSceneList FindList()
        {
            FomoxaSettings settings = FomoxaSettings.Find(out string _);
            if (settings == null || string.IsNullOrWhiteSpace(settings.GeneratedFolder))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<NetworkSceneList>(settings.GeneratedFolder.Trim().TrimEnd('/') + "/" + ListFileName);
        }

        public static void AssignCollected(Scene scene, NetworkSceneList collected)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (NetworkManager manager in root.GetComponentsInChildren<NetworkManager>(true))
                {
                    manager.CollectedScenes = collected;
                }
            }
        }

        public static string Constants(string namespaceName, IReadOnlyList<FomoxaScene> scenes)
        {
            IReadOnlyList<string> names = FomoxaPrefabStep.ConstantNames(scenes.Select(scene => scene.Path).ToList());
            var text = new StringBuilder()
                .Append($"namespace {namespaceName}\n")
                .Append("{\n")
                .Append("    public static class FomoxaScenes\n")
                .Append("    {\n");
            for (int index = 0; index < scenes.Count; index++)
            {
                text.Append($"        public const uint {names[index]}Id = 0x{scenes[index].SceneId:X8};\n");
            }

            return text
                .Append("    }\n")
                .Append("}\n")
                .ToString();
        }

        private static bool Matches(NetworkSceneList list, IReadOnlyList<FomoxaScene> scenes)
        {
            if (list.Count != scenes.Count)
            {
                return false;
            }

            for (int index = 0; index < scenes.Count; index++)
            {
                if (list.SceneIdAt(index) != scenes[index].SceneId || list.PathAt(index) != scenes[index].Path)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool WriteIfChanged(string path, string content)
        {
            if (File.Exists(path) && File.ReadAllText(path) == content)
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content, Utf8WithoutBom);
            return true;
        }
    }
}
