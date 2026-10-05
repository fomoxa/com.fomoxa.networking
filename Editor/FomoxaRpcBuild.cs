using System.Collections.Generic;
using System.IO;
using Fomoxa.Networking;
using UnityEditor.Build.Reporting;
using UnityEditor.Build;
using UnityEngine;

namespace Fomoxa.Unity.Editor
{
    public sealed class FomoxaRpcBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            IReadOnlyList<string> errors = RunForProject();
            if (errors.Count > 0)
            {
                throw new BuildFailedException("[Fomoxa] " + string.Join("\n", errors));
            }
        }

        public static IReadOnlyList<string> RunForProject()
        {
            IReadOnlyList<RpcDeclaration> rpcs = FomoxaRpcScanner.Scan(FomoxaRpcScanner.MethodsForThisEditor(), out string scanError);
            if (rpcs == null)
            {
                return new[] { scanError };
            }

            if (rpcs.Count == 0)
            {
                return new string[0];
            }

            FomoxaSettings settings = FomoxaSettings.Find(out string settingsError);
            if (settings == null)
            {
                return new[] { settingsError ?? "the project declares [NetworkRpc] methods but has no FomoxaSettings asset; create one with Assets > Create > Fomoxa > Settings" };
            }

            return FomoxaGenerator.CheckRpcs(Directory.GetParent(Application.dataPath).FullName, settings.GeneratedFolder, rpcs);
        }
    }
}
