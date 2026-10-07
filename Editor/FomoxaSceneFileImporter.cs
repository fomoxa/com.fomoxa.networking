using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Fomoxa.Unity.Editor
{
    [ScriptedImporter(1, "fomoxascene")]
    public sealed class FomoxaSceneFileImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            var file = new TextAsset(File.ReadAllBytes(context.assetPath));
            context.AddObjectToAsset("scene file", file);
            context.SetMainObject(file);
        }
    }
}
