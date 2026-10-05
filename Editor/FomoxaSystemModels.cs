using System.IO;

namespace Fomoxa.Unity.Editor
{
    public static class FomoxaSystemModels
    {
        public static string SourcePathForThisEditor()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(FomoxaSystemModels).Assembly);
            return package == null ? null : Path.Combine(package.resolvedPath, "Runtime", "Core", "SystemModels");
        }
    }
}
