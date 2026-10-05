using System.IO;
using UnityEngine;

namespace Fomoxa.Unity.Editor
{
    public static class FomoxacBinary
    {
        public static string PathForThisEditor()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(FomoxacBinary).Assembly);
            string relative = RelativePathFor(Application.platform);
            if (package == null || relative == null)
            {
                return null;
            }

            return Path.Combine(package.resolvedPath, "Tools~", "fomoxac", relative);
        }

        private static string RelativePathFor(RuntimePlatform platform)
        {
            switch (platform)
            {
                case RuntimePlatform.WindowsEditor:
                    return Path.Combine("win-x64", "fomoxac.exe");
                case RuntimePlatform.OSXEditor:
                    return Path.Combine("osx", "fomoxac");
                case RuntimePlatform.LinuxEditor:
                    return Path.Combine("linux-x64", "fomoxac");
                default:
                    return null;
            }
        }
    }
}
