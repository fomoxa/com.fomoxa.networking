using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity.Editor
{
    public sealed class FomoxaPrefabBuild : IPreprocessBuildWithReport, IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var errors = new List<string>(FomoxaPrefabStep.RunForProject());
            errors.AddRange(FomoxaSceneStep.RunForProject());
            if (errors.Count > 0)
            {
                throw new BuildFailedException("[Fomoxa] " + string.Join("\n", errors));
            }
        }

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            FomoxaPrefabStep.AssignCollected(scene, FomoxaPrefabStep.FindList());
            FomoxaSceneStep.AssignCollected(scene, FomoxaSceneStep.FindList());
            FomoxaSceneObjectIds.ProcessScene(scene, report != null);
        }
    }
}
