using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Fomoxa.Networking;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity.Editor
{
    public static class FomoxaSceneFileStep
    {
        [MenuItem("Tools/Fomoxa/Export Network Scene Files")]
        public static void RunFromMenu()
        {
            foreach (string error in ExportAll())
            {
                Debug.LogError("[Fomoxa] " + error);
            }
        }

        public static string FilePath(string folder, uint sceneId) => folder.TrimEnd('/') + "/" + sceneId + SceneFileFormat.Extension;

        public static IReadOnlyList<string> ExportAll() => Export(stale: false);

        public static IReadOnlyList<string> ExportStale() => Export(stale: true);

        public static void ExportSaved(Scene scene)
        {
            if (!TryGetContext(out string folder, out List<FomoxaScene> networkScenes, out FomoxaRegistry registry, new List<string>()))
            {
                return;
            }

            FomoxaScene saved = networkScenes.FirstOrDefault(candidate => candidate.Path == scene.path);
            if (saved.Path == null)
            {
                return;
            }

            var errors = new List<string>();
            ExportScene(scene, saved.SceneId, folder, registry, errors);
            errors.AddRange(FomoxaSceneStep.RunForProject());
            foreach (string error in errors)
            {
                Debug.LogError("[Fomoxa] " + error);
            }
        }

        public static SceneFile Describe(Scene scene, uint sceneId, List<string> errors)
        {
            int before = errors.Count;
            var file = new SceneFile { SceneId = sceneId };
            DescribeObjects(scene, file, errors);
            DescribeStaticColliders(scene, file, errors);
            for (int layer = 0; layer < 32; layer++)
            {
                uint mask = 0;
                uint mask2D = 0;
                for (int other = 0; other < 32; other++)
                {
                    if (!Physics.GetIgnoreLayerCollision(layer, other))
                    {
                        mask |= 1u << other;
                    }

                    if (!Physics2D.GetIgnoreLayerCollision(layer, other))
                    {
                        mask2D |= 1u << other;
                    }
                }

                file.LayerCollisions.Add(mask);
                file.LayerCollisions2D.Add(mask2D);
            }

            return errors.Count == before ? file : null;
        }

        public static bool WriteIfChanged(string path, byte[] bytes)
        {
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, bytes);
            return true;
        }

        public static FomoxaRegistry ApplicationRegistry(out string error)
        {
            FomoxaSettings settings = FomoxaSettings.Find(out string _);
            if (settings == null || string.IsNullOrWhiteSpace(settings.GeneratedFolder))
            {
                error = "no FomoxaSettings with a generated folder; scene files are written next to FomoxaSceneList";
                return null;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string namespaceName = FomoxaGenerator.NamespaceOf(Path.Combine(projectRoot, settings.GeneratedFolder.Trim().TrimEnd('/')));
            MethodInfo registerAll = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(namespaceName + ".FomoxaAdapters", false))
                .Where(type => type != null)
                .Select(type => type.GetMethod("RegisterAll", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(FomoxaRegistry) }, null))
                .FirstOrDefault(method => method != null);
            if (registerAll == null)
            {
                error = $"no generated {namespaceName}.FomoxaAdapters.RegisterAll; run the Fomoxa code generation first";
                return null;
            }

            var registry = new FomoxaRegistry();
            registerAll.Invoke(null, new object[] { registry });
            error = null;
            return registry;
        }

        private static IReadOnlyList<string> Export(bool stale)
        {
            var errors = new List<string>();
            if (!TryGetContext(out string folder, out List<FomoxaScene> networkScenes, out FomoxaRegistry registry, errors))
            {
                return errors;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            foreach (FomoxaScene networkScene in networkScenes)
            {
                if (stale && IsCurrent(projectRoot, folder, networkScene, registry))
                {
                    continue;
                }

                Scene scene = SceneManager.GetSceneByPath(networkScene.Path);
                bool opened = !scene.isLoaded;
                if (opened)
                {
                    scene = EditorSceneManager.OpenScene(networkScene.Path, OpenSceneMode.Additive);
                }

                try
                {
                    ExportScene(scene, networkScene.SceneId, folder, registry, errors);
                }
                finally
                {
                    if (opened)
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }
            }

            DeleteFilesOfOtherScenes(projectRoot, folder, networkScenes);
            errors.AddRange(FomoxaSceneStep.RunForProject());
            return errors;
        }

        private static bool TryGetContext(out string folder, out List<FomoxaScene> networkScenes, out FomoxaRegistry registry, List<string> errors)
        {
            folder = null;
            networkScenes = null;
            registry = null;
            FomoxaSettings settings = FomoxaSettings.Find(out string _);
            if (settings == null || string.IsNullOrWhiteSpace(settings.GeneratedFolder))
            {
                return false;
            }

            folder = settings.GeneratedFolder.Trim().TrimEnd('/');
            var valid = new List<FomoxaScene>();
            errors.AddRange(FomoxaSceneStep.Validate(new[] { "Assets" }, valid));
            networkScenes = FomoxaSceneStep.BuildScenes(valid, EditorBuildSettings.scenes);
            registry = ApplicationRegistry(out string error);
            if (registry == null)
            {
                errors.Add(error);
                return false;
            }

            return true;
        }

        private static void ExportScene(Scene scene, uint sceneId, string folder, FomoxaRegistry registry, List<string> errors)
        {
            SceneFile file = Describe(scene, sceneId, errors);
            if (file == null)
            {
                return;
            }

            string assetPath = FilePath(folder, sceneId);
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            if (WriteIfChanged(Path.Combine(projectRoot, assetPath), SceneFileFormat.Write(registry, file)))
            {
                AssetDatabase.ImportAsset(assetPath);
            }
        }

        private static bool IsCurrent(string projectRoot, string folder, FomoxaScene networkScene, FomoxaRegistry registry)
        {
            string filePath = Path.Combine(projectRoot, FilePath(folder, networkScene.SceneId));
            string scenePath = Path.Combine(projectRoot, networkScene.Path);
            if (!File.Exists(filePath) || File.GetLastWriteTimeUtc(filePath) < File.GetLastWriteTimeUtc(scenePath))
            {
                return false;
            }

            try
            {
                SceneFileFormat.Read(registry, File.ReadAllBytes(filePath));
                return true;
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }

        private static void DeleteFilesOfOtherScenes(string projectRoot, string folder, List<FomoxaScene> networkScenes)
        {
            string directory = Path.Combine(projectRoot, folder);
            if (!Directory.Exists(directory))
            {
                return;
            }

            var kept = new HashSet<string>(networkScenes.Select(scene => scene.SceneId + SceneFileFormat.Extension), StringComparer.Ordinal);
            foreach (string path in Directory.GetFiles(directory, "*" + SceneFileFormat.Extension))
            {
                string name = Path.GetFileName(path);
                if (!kept.Contains(name))
                {
                    AssetDatabase.DeleteAsset(folder + "/" + name);
                }
            }
        }

        private static void DescribeObjects(Scene scene, SceneFile file, List<string> errors)
        {
            var objects = new List<NetworkObject>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                objects.AddRange(root.GetComponentsInChildren<NetworkObject>(true));
            }

            foreach (NetworkObject networkObject in objects.OrderBy(candidate => candidate.SceneObjectId))
            {
                string name = $"{scene.path}: {PathOf(networkObject.transform)}";
                if (!PrefabHash.TryDescribeSceneObject(networkObject, out uint fingerprint, out string error))
                {
                    errors.Add($"{name}: {error}");
                    continue;
                }

                if (networkObject.SceneObjectId == 0)
                {
                    errors.Add($"{name} has no scene object id; save the scene");
                    continue;
                }

                Transform transform = networkObject.transform;
                var entry = new SceneFileObject
                {
                    SceneObjectId = networkObject.SceneObjectId,
                    Fingerprint = fingerprint,
                    Pose = PoseOf(transform),
                };
                foreach (NetworkBehaviour behaviour in networkObject.GetComponentsInChildren<NetworkBehaviour>(true))
                {
                    entry.BehaviourTypes.Add(behaviour.GetType().FullName);
                }

                try
                {
                    if (BodyDescriptions.TryDescribe(networkObject.gameObject, out BodyDesc body))
                    {
                        entry.Body = SceneFileGeometry.ToFile(body);
                    }

                    if (BodyDescriptions.TryDescribe2D(networkObject.gameObject, out BodyDesc2D body2D))
                    {
                        entry.Body2D = SceneFileGeometry.ToFile(body2D);
                    }
                }
                catch (Exception exception) when (exception is NotSupportedException || exception is ArgumentException)
                {
                    errors.Add($"{name}: {exception.Message}");
                    continue;
                }

                file.Objects.Add(entry);
            }
        }

        private static void DescribeStaticColliders(Scene scene, SceneFile file, List<string> errors)
        {
            var colliders = new List<ColliderDesc>();
            var colliders2D = new List<ColliderDesc2D>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Collider collider in root.GetComponentsInChildren<Collider>())
                {
                    if (!collider.enabled || collider.attachedRigidbody != null || collider.GetComponentInParent<NetworkObject>(true) != null)
                    {
                        continue;
                    }

                    try
                    {
                        if (collider is TerrainCollider terrain)
                        {
                            DescribeTerrain(terrain, colliders);
                        }
                        else
                        {
                            BodyDescriptions.DescribeStatic(collider, null, colliders);
                        }
                    }
                    catch (Exception exception) when (exception is NotSupportedException || exception is ArgumentException)
                    {
                        errors.Add($"{scene.path}: {PathOf(collider.transform)}: {exception.Message}");
                    }
                }

                foreach (Collider2D collider in root.GetComponentsInChildren<Collider2D>())
                {
                    if (!collider.enabled || collider.attachedRigidbody != null || collider.GetComponentInParent<NetworkObject>(true) != null)
                    {
                        continue;
                    }

                    try
                    {
                        BodyDescriptions.DescribeStatic2D(collider, null, colliders2D);
                    }
                    catch (Exception exception) when (exception is NotSupportedException || exception is ArgumentException)
                    {
                        errors.Add($"{scene.path}: {PathOf(collider.transform)}: {exception.Message}");
                    }
                }
            }

            foreach (ColliderDesc collider in colliders)
            {
                file.Colliders.Add(SceneFileGeometry.ToFile(collider));
            }

            foreach (ColliderDesc2D collider in colliders2D)
            {
                file.Colliders2D.Add(SceneFileGeometry.ToFile(collider));
            }
        }

        private static void DescribeTerrain(TerrainCollider terrain, List<ColliderDesc> into)
        {
            TerrainData data = terrain.terrainData;
            if (data == null)
            {
                throw new ArgumentException("the TerrainCollider has no TerrainData");
            }

            int resolution = data.heightmapResolution;
            float[,] heights = data.GetHeights(0, 0, resolution, resolution);
            bool[,] solid = data.GetHoles(0, 0, resolution - 1, resolution - 1);
            Vector3 size = data.size;
            var vertices = new System.Numerics.Vector3[resolution * resolution];
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    vertices[z * resolution + x] = new System.Numerics.Vector3(x * size.x / (resolution - 1), heights[z, x] * size.y, z * size.z / (resolution - 1));
                }
            }

            var triangles = new List<int>();
            for (int z = 0; z < resolution - 1; z++)
            {
                for (int x = 0; x < resolution - 1; x++)
                {
                    if (!solid[z, x])
                    {
                        continue;
                    }

                    int corner = z * resolution + x;
                    triangles.Add(corner);
                    triangles.Add(corner + resolution);
                    triangles.Add(corner + resolution + 1);
                    triangles.Add(corner);
                    triangles.Add(corner + resolution + 1);
                    triangles.Add(corner + 1);
                }
            }

            if (triangles.Count == 0)
            {
                return;
            }

            into.Add(new ColliderDesc(
                BodyShape.TriangleMesh(vertices, triangles),
                terrain.transform.position.ToNumerics(),
                System.Numerics.Quaternion.Identity,
                ColliderMaterials.FromUnity(terrain.sharedMaterial),
                terrain.gameObject.layer,
                terrain.isTrigger));
        }

        private static SceneFilePose PoseOf(Transform transform)
        {
            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;
            Vector3 scale = transform.lossyScale;
            return new SceneFilePose
            {
                PositionX = position.x,
                PositionY = position.y,
                PositionZ = position.z,
                RotationX = rotation.x,
                RotationY = rotation.y,
                RotationZ = rotation.z,
                RotationW = rotation.w,
                ScaleX = scale.x,
                ScaleY = scale.y,
                ScaleZ = scale.z,
            };
        }

        private static string PathOf(Transform transform) =>
            transform.parent == null ? transform.name : PathOf(transform.parent) + "/" + transform.name;
    }
}
