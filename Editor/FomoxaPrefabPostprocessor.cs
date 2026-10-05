using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Fomoxa.Unity.Editor
{
    public sealed class FomoxaPrefabPostprocessor : AssetPostprocessor
    {
        private static bool scheduled;
        private static bool scenesScheduled;

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (Application.isBatchMode)
            {
                return;
            }

            string[] changed = importedAssets.Concat(deletedAssets).Concat(movedAssets).ToArray();
            if (!scheduled && changed.Any(IsPrefab))
            {
                scheduled = true;
                EditorApplication.delayCall += RunScheduled;
            }

            if (!scenesScheduled && changed.Any(IsScene))
            {
                ScheduleScenes();
            }
        }

        internal static void ScheduleScenes()
        {
            if (scenesScheduled || Application.isBatchMode)
            {
                return;
            }

            scenesScheduled = true;
            EditorApplication.delayCall += RunScenesScheduled;
        }

        private static bool IsPrefab(string path) => path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);

        private static bool IsScene(string path) => path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase);

        private static void RunScenesScheduled()
        {
            scenesScheduled = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            FomoxaSceneStep.RunAndLog();
        }

        private static void RunScheduled()
        {
            scheduled = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            FomoxaPrefabStep.RunAndLog();
        }
    }
}
