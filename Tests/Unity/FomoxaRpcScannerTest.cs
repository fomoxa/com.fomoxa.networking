using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System;
using Fomoxa.Networking;
using Fomoxa.Unity.Editor;
using Fomoxa.Unity.Tests.Support;
using NUnit.Framework;

namespace Fomoxa.Unity.Tests
{
    public sealed partial class FomoxaRpcScannerTest
    {
        private sealed class Player : NetworkBehaviour
        {
            [NetworkRpc]
            private void Jump()
            {
            }

            [NetworkRpc("PlayerWave", Channel = Channel.Unreliable)]
            private void Greet()
            {
            }

            private void NotAnRpc()
            {
            }
        }

        private sealed class Vehicle : NetworkBehaviour
        {
            [NetworkRpc]
            private void Jump()
            {
            }
        }

        private sealed class Clash : NetworkBehaviour
        {
            [NetworkRpc("PlayerJump")]
            private void Leap()
            {
            }
        }

        private sealed class NotABehaviour
        {
            [NetworkRpc]
            private void Jump()
            {
            }
        }

        private sealed class WithParameter : NetworkBehaviour
        {
            [NetworkRpc]
            private void Jump(int height)
            {
            }
        }

        private sealed class BadName : NetworkBehaviour
        {
            [NetworkRpc("Not-A-Name")]
            private void Jump()
            {
            }
        }

        private sealed class Generic<T> : NetworkBehaviour
        {
            [NetworkRpc]
            private void Jump()
            {
            }
        }

        private sealed partial class Attributed : NetworkBehaviour
        {
            [ServerRpc]
            private void Jump()
            {
            }

            [ServerRpc("AttributedLeap", Channel = Channel.Unreliable)]
            private void Hop(ulong peerId)
            {
            }

            [ClientRpc]
            private void Ping()
            {
            }

            [ServerRpc(Codec = "net")]
            private void Fire(ulong peerId, GeneratedValue value)
            {
            }

            [ClientRpc]
            private void Announce(GeneratedNotice notice)
            {
            }
        }

        private sealed partial class ShadowPlayer : NetworkBehaviour
        {
            [ClientRpc("PlayerJump")]
            private void Leap()
            {
            }
        }

        [Test]
        public void DefaultModelIsTypeAndMethodNameAndDefaultChannelIsReliable()
        {
            IReadOnlyList<RpcDeclaration> rpcs = FomoxaRpcScanner.Scan(Methods(typeof(Player)), out string error);

            Assert.IsNull(error);
            Assert.AreEqual(2, rpcs.Count);
            Assert.AreEqual(typeof(Player).FullName, rpcs[0].Type);
            Assert.AreEqual("Greet", rpcs[0].Method);
            Assert.AreEqual("PlayerWave", rpcs[0].Model);
            Assert.AreEqual(Channel.Unreliable, rpcs[0].Channel);
            Assert.AreEqual("Jump", rpcs[1].Method);
            Assert.AreEqual("PlayerJump", rpcs[1].Model);
            Assert.AreEqual(Channel.ReliableOrdered, rpcs[1].Channel);
        }

        [Test]
        public void SameMethodNameInTwoTypesGivesTwoModels()
        {
            IReadOnlyList<RpcDeclaration> rpcs = FomoxaRpcScanner.Scan(Methods(typeof(Vehicle), typeof(Player)), out string error);

            Assert.IsNull(error);
            CollectionAssert.AreEquivalent(new[] { "PlayerJump", "PlayerWave", "VehicleJump" }, rpcs.Select(rpc => rpc.Model));
        }

        [Test]
        public void ServerRpcAndClientRpcWithoutAModelAreParameterlessRpcsAndRpcsWithAModelAreSkipped()
        {
            IReadOnlyList<RpcDeclaration> rpcs = FomoxaRpcScanner.Scan(Methods(typeof(Attributed)), out string error);

            Assert.IsNull(error);
            Assert.AreEqual(new[] { "Hop", "Jump", "Ping" }, rpcs.Select(rpc => rpc.Method));
            Assert.AreEqual(new[] { "AttributedLeap", "AttributedJump", "AttributedPing" }, rpcs.Select(rpc => rpc.Model));
            Assert.AreEqual(new[] { Channel.Unreliable, Channel.ReliableOrdered, Channel.ReliableOrdered }, rpcs.Select(rpc => rpc.Channel));
        }

        [Test]
        public void ClientRpcClashingWithANetworkRpcModelIsAnErrorNamingItsAttribute()
        {
            Assert.IsNull(FomoxaRpcScanner.Scan(Methods(typeof(Player), typeof(ShadowPlayer)), out string error));
            StringAssert.Contains("both name their model \"PlayerJump\"; give one of them another name with [ClientRpc(\"...\")]", error);
        }

        [Test]
        public void TwoRpcsWithOneModelNameAreAnError()
        {
            Assert.IsNull(FomoxaRpcScanner.Scan(Methods(typeof(Player), typeof(Clash)), out string error));
            StringAssert.Contains("both name their model \"PlayerJump\"", error);
            StringAssert.Contains(typeof(Clash).FullName + ".Leap", error);
        }

        [Test]
        public void MethodOutsideANetworkBehaviourIsAnError()
        {
            Assert.IsNull(FomoxaRpcScanner.Scan(Methods(typeof(NotABehaviour)), out string error));
            StringAssert.Contains("is not a method of a NetworkBehaviour", error);
        }

        [Test]
        public void MethodWithAParameterIsAnError()
        {
            Assert.IsNull(FomoxaRpcScanner.Scan(Methods(typeof(WithParameter)), out string error));
            StringAssert.Contains("has parameters", error);
        }

        [Test]
        public void ModelNameThatIsNotAnIdentifierIsAnError()
        {
            Assert.IsNull(FomoxaRpcScanner.Scan(Methods(typeof(BadName)), out string error));
            StringAssert.Contains("\"Not-A-Name\", which is not a C# identifier", error);
        }

        [Test]
        public void MethodOfAGenericTypeIsAnError()
        {
            Assert.IsNull(FomoxaRpcScanner.Scan(Methods(typeof(Generic<>)), out string error));
            StringAssert.Contains("is generic", error);
        }

        [Test]
        public void TheEditorDoesNotScanThePackageOrEditorAssemblies()
        {
            IReadOnlyList<MethodInfo> methods = FomoxaRpcScanner.MethodsForThisEditor();

            CollectionAssert.DoesNotContain(methods.Select(method => method.DeclaringType), typeof(Player));
            CollectionAssert.DoesNotContain(methods.Select(method => method.DeclaringType), typeof(EmptyRpcBehaviour));
            Assert.IsFalse(methods.Any(method => method.DeclaringType.Assembly.GetName().Name.StartsWith("Fomoxa.Unity", StringComparison.Ordinal)));
        }

        private static IEnumerable<MethodInfo> Methods(params Type[] types) =>
            types.SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
    }
}
