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
    public static class FomoxaPrefabStep
    {
        public const string ConstantsFileName = "FomoxaPrefabs.cs";
        public const string ListFileName = "FomoxaPrefabList.asset";

        private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false);

        [MenuItem("Tools/Fomoxa/Assign Prefab Ids")]
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
            var prefabs = new List<FomoxaPrefab>();
            List<string> errors = AssignAndValidate(new[] { "Assets" }, prefabs);
            FomoxaSettings settings = FomoxaSettings.Find(out string _);
            if (settings == null || string.IsNullOrWhiteSpace(settings.GeneratedFolder))
            {
                return errors;
            }

            string generatedFolder = settings.GeneratedFolder.Trim().TrimEnd('/');
            WriteList(generatedFolder + "/" + ListFileName, prefabs.Select(prefab => prefab.Prefab).ToList());
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string namespaceName = FomoxaGenerator.NamespaceOf(Path.Combine(projectRoot, generatedFolder));
            if (namespaceName != null)
            {
                string constantsPath = generatedFolder + "/" + ConstantsFileName;
                if (WriteIfChanged(Path.Combine(projectRoot, constantsPath), Constants(namespaceName, prefabs)))
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

        public static List<string> AssignAndValidate(string[] folders, List<FomoxaPrefab> valid)
        {
            var errors = new List<string>();
            var owners = new Dictionary<uint, string>();
            IEnumerable<string> paths = AssetDatabase.FindAssets("t:Prefab", folders)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .OrderBy(path => path, StringComparer.Ordinal);
            foreach (string path in paths)
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                NetworkObject networkObject = root == null ? null : root.GetComponentInChildren<NetworkObject>(true);
                if (networkObject == null)
                {
                    continue;
                }

                if (!PrefabHash.TryDescribe(networkObject, out uint fingerprint, out string error))
                {
                    errors.Add($"{path}: {error}");
                    continue;
                }

                if (!networkObject.ExplicitPrefabId)
                {
                    AssignId(networkObject, PrefabIdOf(AssetDatabase.AssetPathToGUID(path)));
                }

                ClearSceneObjectId(networkObject);

                uint prefabId = networkObject.PrefabId;
                if (prefabId == 0)
                {
                    errors.Add($"{path}: the prefab id is 0; give the NetworkObject an explicit prefab id");
                    continue;
                }

                if (owners.TryGetValue(prefabId, out string owner))
                {
                    errors.Add($"{path}: prefab id 0x{prefabId:X8} is already used by {owner}");
                    continue;
                }

                owners.Add(prefabId, path);
                valid.Add(new FomoxaPrefab(path, networkObject, prefabId, fingerprint));
            }

            return errors;
        }

        public static uint PrefabIdOf(string guid) => PrefabHash.Fnv1a(guid);

        public static void WriteList(string assetPath, IReadOnlyList<NetworkObject> prefabs)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabList>(assetPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabList>();
                list.Set(prefabs);
                AssetDatabase.CreateAsset(list, assetPath);
                return;
            }

            if (list.Prefabs.SequenceEqual(prefabs))
            {
                return;
            }

            list.Set(prefabs);
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssetIfDirty(list);
        }

        public static NetworkPrefabList FindList()
        {
            FomoxaSettings settings = FomoxaSettings.Find(out string _);
            if (settings == null || string.IsNullOrWhiteSpace(settings.GeneratedFolder))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<NetworkPrefabList>(settings.GeneratedFolder.Trim().TrimEnd('/') + "/" + ListFileName);
        }

        public static void AssignCollected(Scene scene, NetworkPrefabList collected)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (NetworkManager manager in root.GetComponentsInChildren<NetworkManager>(true))
                {
                    manager.CollectedPrefabs = manager.CollectPrefabs ? collected : null;
                }
            }
        }

        public static string Constants(string namespaceName, IReadOnlyList<FomoxaPrefab> prefabs)
        {
            IReadOnlyList<string> names = ConstantNames(prefabs.Select(prefab => prefab.Path).ToList());
            var text = new StringBuilder()
                .Append($"namespace {namespaceName}\n")
                .Append("{\n")
                .Append("    public static class FomoxaPrefabs\n")
                .Append("    {\n");
            for (int index = 0; index < prefabs.Count; index++)
            {
                if (index > 0)
                {
                    text.Append("\n");
                }

                text.Append($"        public const uint {names[index]}Id = 0x{prefabs[index].PrefabId:X8};\n")
                    .Append($"        public const uint {names[index]}Fingerprint = 0x{prefabs[index].Fingerprint:X8};\n");
            }

            return text
                .Append("    }\n")
                .Append("}\n")
                .ToString();
        }

        public static IReadOnlyList<string> ConstantNames(IReadOnlyList<string> paths)
        {
            var names = paths.Select(path => Identifier(Path.GetFileNameWithoutExtension(path))).ToList();
            var repeated = new HashSet<string>(names.GroupBy(name => name).Where(group => group.Count() > 1).Select(group => group.Key));
            for (int index = 0; index < names.Count; index++)
            {
                if (repeated.Contains(names[index]))
                {
                    names[index] = Identifier(FolderQualifiedName(paths[index]));
                }
            }

            return names;
        }

        private static string FolderQualifiedName(string path)
        {
            string withoutExtension = Path.ChangeExtension(path.Replace('\\', '/'), null);
            return withoutExtension.StartsWith("Assets/", StringComparison.Ordinal)
                ? withoutExtension.Substring("Assets/".Length)
                : withoutExtension;
        }

        private static string Identifier(string name)
        {
            var text = new StringBuilder(name.Length + 1);
            foreach (char character in name)
            {
                text.Append(char.IsLetterOrDigit(character) && character < 128 ? character : '_');
            }

            if (text.Length == 0 || char.IsDigit(text[0]))
            {
                text.Insert(0, '_');
            }

            return text.ToString();
        }

        private static void AssignId(NetworkObject networkObject, uint prefabId)
        {
            if (networkObject.PrefabId == prefabId)
            {
                return;
            }

            var serialized = new SerializedObject(networkObject);
            serialized.FindProperty("prefabId").uintValue = prefabId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SavePrefabAsset(networkObject.gameObject);
        }

        private static void ClearSceneObjectId(NetworkObject networkObject)
        {
            if (networkObject.SceneObjectId == 0)
            {
                return;
            }

            var serialized = new SerializedObject(networkObject);
            serialized.FindProperty("sceneObjectId").ulongValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SavePrefabAsset(networkObject.gameObject);
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
