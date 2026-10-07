using UnityEditor;
using UnityEditor.SceneManagement;

namespace Fomoxa.Unity.Editor
{
    [InitializeOnLoad]
    public static class FomoxaSceneSaveHook
    {
        static FomoxaSceneSaveHook()
        {
            EditorSceneManager.sceneSaving += (scene, path) =>
                FomoxaSceneObjectIds.Assign(scene, FomoxaSceneObjectIds.SceneHashOf(AssetDatabase.AssetPathToGUID(path)));
            EditorBuildSettings.sceneListChanged += FomoxaPrefabPostprocessor.ScheduleScenes;
            EditorSceneManager.sceneSaved += FomoxaSceneFileStep.ExportSaved;
        }
    }
}
