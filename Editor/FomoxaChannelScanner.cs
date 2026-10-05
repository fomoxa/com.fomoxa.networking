using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System;
using Fomoxa.Networking;
using UnityEditor.Compilation;
using UnityEditor;

namespace Fomoxa.Unity.Editor
{
    public static class FomoxaChannelScanner
    {
        public static IReadOnlyList<Type> TypesForThisEditor(string modelsFolder)
        {
            var assemblies = new HashSet<string> { typeof(NetworkChannelAttribute).Assembly.GetName().Name };
            if (AssetDatabase.IsValidFolder(modelsFolder))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:MonoScript", new[] { modelsFolder }))
                {
                    string assembly = CompilationPipeline.GetAssemblyNameFromScriptPath(AssetDatabase.GUIDToAssetPath(guid));
                    if (assembly != null)
                    {
                        assemblies.Add(Path.GetFileNameWithoutExtension(assembly));
                    }
                }
            }

            return TypeCache.GetTypesWithAttribute<NetworkChannelAttribute>()
                .Where(type => assemblies.Contains(type.Assembly.GetName().Name) && type.Namespace != FomoxaGenerator.RpcModelsNamespace)
                .ToList();
        }

        public static IReadOnlyList<ChannelDeclaration> Scan(IEnumerable<Type> types, out string error)
        {
            var declarations = new List<ChannelDeclaration>();
            foreach (Type type in types.OrderBy(type => type.FullName, StringComparer.Ordinal))
            {
                NetworkChannelAttribute[] channels = type.GetCustomAttributes<NetworkChannelAttribute>(false).ToArray();
                if (channels.Length == 0)
                {
                    continue;
                }

                if (type.GetCustomAttribute<NetworkAttribute>(false) == null)
                {
                    error = $"{type.FullName} declares a network channel but is not a [Network] model";
                    return null;
                }

                var codecs = new HashSet<string>(type.GetCustomAttributes<CodecAttribute>(false).SelectMany(codec => codec.Names));
                var declared = new HashSet<string>();
                foreach (NetworkChannelAttribute channel in channels.OrderBy(channel => channel.Codec, StringComparer.Ordinal))
                {
                    if (!codecs.Contains(channel.Codec))
                    {
                        error = $"{type.FullName} declares a network channel for codec \"{channel.Codec}\", which it does not declare with [Codec]";
                        return null;
                    }

                    if (!declared.Add(channel.Codec))
                    {
                        error = $"{type.FullName} declares more than one network channel for codec \"{channel.Codec}\"";
                        return null;
                    }

                    if (channel.Channel != Channel.Unreliable)
                    {
                        declarations.Add(new ChannelDeclaration(type.Name, channel.Codec, channel.Channel));
                    }
                }
            }

            error = null;
            return declarations;
        }
    }
}
