using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System;
using Fomoxa.Networking;
using UnityEditor.Compilation;
using UnityEditor;

namespace Fomoxa.Unity.Editor
{
    public static class FomoxaRpcScanner
    {
        private static readonly string[] PackageAssemblyPrefixes = { "Fomoxa.Unity", "Fomoxa.Networking" };
        private const string NetworkRpcName = "NetworkRpc";

        private static readonly Regex Identifier = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$");

        public static IReadOnlyList<MethodInfo> MethodsForThisEditor()
        {
            var assemblies = new HashSet<string>(
                CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies)
                    .Select(assembly => assembly.name)
                    .Where(name => !PackageAssemblyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal))));
            return TypeCache.GetMethodsWithAttribute<NetworkRpcAttribute>()
                .Concat(TypeCache.GetMethodsWithAttribute<ServerRpcAttribute>())
                .Concat(TypeCache.GetMethodsWithAttribute<ClientRpcAttribute>())
                .Distinct()
                .Where(method => assemblies.Contains(method.DeclaringType.Assembly.GetName().Name))
                .ToList();
        }

        public static IReadOnlyList<RpcDeclaration> Scan(IEnumerable<MethodInfo> methods, out string error)
        {
            var declarations = new List<RpcDeclaration>();
            var byModel = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
            foreach (MethodInfo method in methods
                .Distinct()
                .OrderBy(method => method.DeclaringType.FullName, StringComparer.Ordinal)
                .ThenBy(method => method.Name, StringComparer.Ordinal))
            {
                if (!TryReadAttribute(method, out string attributeName, out string declaredModel, out Channel channel))
                {
                    continue;
                }

                Type type = method.DeclaringType;
                string where = Describe(method);
                if (!typeof(NetworkBehaviour).IsAssignableFrom(type))
                {
                    error = $"[{attributeName}] {where} is not a method of a NetworkBehaviour";
                    return null;
                }

                if (type.ContainsGenericParameters || method.IsGenericMethodDefinition)
                {
                    error = $"[{attributeName}] {where} is generic; a parameterless RPC is a non-generic method of a non-generic NetworkBehaviour";
                    return null;
                }

                if (attributeName == NetworkRpcName && method.GetParameters().Length != 0)
                {
                    error = $"[{attributeName}] {where} has parameters; an RPC with a parameter takes a [Network] model and is registered with its adapter";
                    return null;
                }

                string model = declaredModel ?? type.Name + method.Name;
                if (!Identifier.IsMatch(model))
                {
                    error = $"[{attributeName}] {where} names its model \"{model}\", which is not a C# identifier";
                    return null;
                }

                if (byModel.TryGetValue(model, out MethodInfo other))
                {
                    error = $"[{attributeName}] {Describe(other)} and {where} both name their model \"{model}\"; give one of them another name with [{attributeName}(\"...\")]";
                    return null;
                }

                byModel.Add(model, method);
                declarations.Add(new RpcDeclaration(type.FullName, method.Name, model, channel));
            }

            error = null;
            return declarations;
        }

        private static bool TryReadAttribute(MethodInfo method, out string attributeName, out string model, out Channel channel)
        {
            NetworkRpcAttribute networkRpc = method.GetCustomAttribute<NetworkRpcAttribute>(false);
            if (networkRpc != null)
            {
                attributeName = NetworkRpcName;
                model = networkRpc.Model;
                channel = networkRpc.Channel;
                return true;
            }

            ParameterInfo[] parameters = method.GetParameters();
            ServerRpcAttribute serverRpc = method.GetCustomAttribute<ServerRpcAttribute>(false);
            if (serverRpc != null && (parameters.Length == 0 || (parameters.Length == 1 && parameters[0].ParameterType == typeof(ulong))))
            {
                attributeName = "ServerRpc";
                model = serverRpc.Model;
                channel = serverRpc.Channel;
                return true;
            }

            ClientRpcAttribute clientRpc = method.GetCustomAttribute<ClientRpcAttribute>(false);
            if (clientRpc != null && parameters.Length == 0)
            {
                attributeName = "ClientRpc";
                model = clientRpc.Model;
                channel = clientRpc.Channel;
                return true;
            }

            attributeName = null;
            model = null;
            channel = default;
            return false;
        }

        private static string Describe(MethodInfo method) => method.DeclaringType.FullName + "." + method.Name;
    }
}
