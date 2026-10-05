using UnityEditor;
using UnityEngine;

namespace Fomoxa.Unity.Editor
{
    [CreateAssetMenu(fileName = "FomoxaSettings", menuName = "Fomoxa/Settings")]
    public sealed class FomoxaSettings : ScriptableObject
    {
        [SerializeField] private string modelsFolder = "Assets/Models";
        [SerializeField] private string generatedFolder = "Assets/Generated";

        public string ModelsFolder => modelsFolder;

        public string GeneratedFolder => generatedFolder;

        public static FomoxaSettings Find(out string error)
        {
            error = null;
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(FomoxaSettings));
            if (guids.Length == 0)
            {
                return null;
            }

            if (guids.Length > 1)
            {
                error = "more than one FomoxaSettings asset exists; keep exactly one";
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<FomoxaSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
