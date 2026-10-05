using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Fomoxa.Unity.Editor
{
    [InitializeOnLoad]
    public static class FomoxaHook
    {
        private static bool compilationFailed;

        static FomoxaHook()
        {
            CompilationPipeline.compilationStarted += context => compilationFailed = false;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            EditorApplication.delayCall += RunAfterCompilation;
        }

        [MenuItem("Tools/Fomoxa/Generate")]
        public static void GenerateFromMenu()
        {
            FomoxaSettings settings = FomoxaSettings.Find(out string error);
            if (settings == null)
            {
                Debug.LogError("[Fomoxa] " + (error ?? "no FomoxaSettings asset; create one with Assets > Create > Fomoxa > Settings"));
                return;
            }

            Generate(settings);
        }

        private static void OnAssemblyCompiled(string assemblyPath, CompilerMessage[] messages)
        {
            foreach (CompilerMessage message in messages)
            {
                if (message.type == CompilerMessageType.Error)
                {
                    compilationFailed = true;
                    return;
                }
            }
        }

        private static void OnCompilationFinished(object context)
        {
            if (compilationFailed)
            {
                EditorApplication.delayCall += RunAfterCompilation;
            }
        }

        private static void RunAfterCompilation()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            FomoxaSettings settings = FomoxaSettings.Find(out string error);
            if (error != null)
            {
                Debug.LogError("[Fomoxa] " + error);
            }

            if (settings != null)
            {
                Generate(settings);
            }
        }

        private static void Generate(FomoxaSettings settings)
        {
            IReadOnlyList<ChannelDeclaration> channels = FomoxaChannelScanner.Scan(
                FomoxaChannelScanner.TypesForThisEditor(settings.ModelsFolder),
                out string channelError);
            if (channels == null)
            {
                Debug.LogError("[Fomoxa] " + channelError);
                return;
            }

            IReadOnlyList<RpcDeclaration> rpcs = FomoxaRpcScanner.Scan(FomoxaRpcScanner.MethodsForThisEditor(), out string rpcError);
            if (rpcs == null)
            {
                Debug.LogError("[Fomoxa] " + rpcError);
                return;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            FomoxaGenerateResult result = FomoxaGenerator.Run(
                projectRoot,
                settings.ModelsFolder,
                settings.GeneratedFolder,
                FomoxacBinary.PathForThisEditor(),
                FomoxaSystemModels.SourcePathForThisEditor(),
                channels,
                rpcs);

            if (!result.Succeeded)
            {
                Debug.LogError("[Fomoxa] " + result.Error);
                return;
            }

            if (result.Warnings != null)
            {
                Debug.LogWarning("[Fomoxa] " + result.Warnings);
            }

            AssetDatabase.Refresh();
            if (!FomoxaPrefabStep.OutputsExist())
            {
                FomoxaPrefabStep.RunAndLog();
            }

            if (!FomoxaSceneStep.OutputsExist())
            {
                FomoxaSceneStep.RunAndLog();
            }
        }
    }
}
