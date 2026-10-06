using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System;
using Fomoxa.Networking;
using Fomoxa.Unity.Editor;
using NUnit.Framework;

namespace Fomoxa.Unity.Tests
{
    public sealed class FomoxaGeneratorTest
    {
        private const string GreetingModel =
            "using Fomoxa;\n"
            + "\n"
            + "namespace Sample.Models\n"
            + "{\n"
            + "    [Network]\n"
            + "    [Codec(\"net\")]\n"
            + "    public class Greeting\n"
            + "    {\n"
            + "        [Network(\"u32\")]\n"
            + "        [Codec(\"net\")]\n"
            + "        public uint Value { get; set; }\n"
            + "    }\n"
            + "}\n";

        private const string TwoCodecModel =
            "using Fomoxa;\n"
            + "\n"
            + "namespace Sample.Models\n"
            + "{\n"
            + "    [Network]\n"
            + "    [Codec(\"net\")]\n"
            + "    [Codec(\"wide\")]\n"
            + "    public class Position\n"
            + "    {\n"
            + "        [Network(\"u32\")]\n"
            + "        [Codec(\"net\")]\n"
            + "        [Codec(\"wide\")]\n"
            + "        public uint X { get; set; }\n"
            + "    }\n"
            + "}\n";

        private string projectRoot;

        private static readonly RpcDeclaration Jump = new RpcDeclaration("Game.Player", "Jump", "PlayerJump", Channel.ReliableOrdered);

        private static readonly RpcDeclaration Wave = new RpcDeclaration("Game.Player+Hand", "Wave", "HandWave", Channel.Unreliable);

        private FomoxaGenerateResult Generate(params ChannelDeclaration[] channels) =>
            FomoxaGenerator.Run(
                projectRoot,
                "Models",
                "Generated",
                FomoxacBinary.PathForThisEditor(),
                FomoxaSystemModels.SourcePathForThisEditor(),
                channels,
                new RpcDeclaration[0]);

        private FomoxaGenerateResult GenerateRpcs(params RpcDeclaration[] rpcs) =>
            FomoxaGenerator.Run(
                projectRoot,
                "Models",
                "Generated",
                FomoxacBinary.PathForThisEditor(),
                FomoxaSystemModels.SourcePathForThisEditor(),
                new ChannelDeclaration[0],
                rpcs);

        private string Read(params string[] parts) => File.ReadAllText(Path.Combine(projectRoot, Path.Combine(parts)));

        [SetUp]
        public void CreateProject()
        {
            projectRoot = Path.GetFullPath(Path.Combine("Temp", "FomoxaGeneratorTest", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(Path.Combine(projectRoot, "Models"));
            File.WriteAllText(Path.Combine(projectRoot, "Models", "Greeting.cs"), GreetingModel);
        }

        [TearDown]
        public void DeleteProject()
        {
            Directory.Delete(projectRoot, true);
        }

        [Test]
        public void TheBinaryForThisEditorIsInThePackage()
        {
            Assert.IsTrue(File.Exists(FomoxacBinary.PathForThisEditor()), FomoxacBinary.PathForThisEditor());
        }

        [Test]
        public void TheSystemModelsForThisEditorAreInThePackage()
        {
            Assert.IsTrue(File.Exists(Path.Combine(FomoxaSystemModels.SourcePathForThisEditor(), "MessageBundle.cs")));
        }

        [Test]
        public void FirstRunWritesTheTomlTheCodecsAndTheRegistration()
        {
            FomoxaGenerateResult result = Generate();

            Assert.IsTrue(result.Succeeded, result.Error);
            Assert.IsTrue(result.Changed);
            StringAssert.StartsWith(FomoxaGenerator.TomlMarker, Read("fomoxa.toml"));
            StringAssert.Contains("net_schema = true", Read("fomoxa.toml"));
            StringAssert.Contains("src = [\"Models\", \"FomoxaSystemModels\", \"Generated/RpcModels\"]", Read("fomoxa.toml"));
            Assert.AreEqual("", Read("Generated", FomoxaGenerator.RpcModelsFolder, FomoxaGenerator.KeepFileName));
            Assert.IsEmpty(Directory.GetFiles(Path.Combine(projectRoot, "Generated", FomoxaGenerator.RpcModelsFolder), "*.cs"));
            StringAssert.DoesNotContain("registry.Rpcs.Set", Read("Generated", FomoxaGenerator.AdaptersFileName));
            StringAssert.StartsWith(FomoxaGenerator.SystemModelMarker, Read(FomoxaGenerator.SystemModelsFolder, "MessageBundle.cs"));
            StringAssert.Contains("public const uint MessageId", Read("Generated", "MessageBundleNetCodec.cs"));
            StringAssert.Contains("IMessageCodec<global::Fomoxa.Networking.Messaging.MessageBundle>", Read("Generated", "MessageBundleNetAdapter.cs"));
            StringAssert.Contains("IMessageCodec<global::Fomoxa.Networking.Messaging.ReliableAck>", Read("Generated", "ReliableAckNetAdapter.cs"));
            StringAssert.Contains("registry.SetCodec(MessageBundleNetAdapter.Instance);", Read("Generated", FomoxaGenerator.AdaptersFileName));
            StringAssert.Contains("registry.SetCodec(ReliableAckNetAdapter.Instance);", Read("Generated", FomoxaGenerator.AdaptersFileName));
            foreach (string model in new[] { "LocalPeer", "ObjectSpawn", "ObjectSceneSpawn", "ObjectDespawn", "ObjectOwnerChange", "StateDelta", "StateResync", "TransformUpdate", "TransformSettle", "AnimatorState", "SceneLoad", "SceneUnload", "SceneLoaded", "TickPing", "TickPong", "InputFrames", "ReconcileState", "PeerLeave", "SceneFile" })
            {
                StringAssert.Contains($"IMessageCodec<global::Fomoxa.Networking.Messaging.{model}>", Read("Generated", model + "NetAdapter.cs"));
                StringAssert.Contains($"registry.SetCodec({model}NetAdapter.Instance);", Read("Generated", FomoxaGenerator.AdaptersFileName));
            }

            StringAssert.DoesNotContain("registry.Channels.Set", Read("Generated", FomoxaGenerator.AdaptersFileName));
            Assert.IsTrue(File.Exists(Path.Combine(projectRoot, ".fomoxa", "schema.json")));
            StringAssert.Contains("public static global::Fomoxa.Net.Schema Build()", Read("Generated", "NetSchema.cs"));
            StringAssert.Contains("namespace Generated", Read("Generated", FomoxaGenerator.AdaptersFileName));
            StringAssert.Contains("registry.SetSchema(NetSchema.Build());", Read("Generated", FomoxaGenerator.AdaptersFileName));
            StringAssert.StartsWith("#if UNITY_5_3_OR_NEWER", Read("Generated", FomoxaGenerator.RegistrationFileName));
        }

        [Test]
        public void SecondRunChangesNothingAndFomoxacCheckAgreesOutsideUnity()
        {
            Generate();

            FomoxaGenerateResult second = Generate();

            Assert.IsTrue(second.Succeeded, second.Error);
            Assert.IsFalse(second.Changed);
            var check = new ProcessStartInfo(FomoxacBinary.PathForThisEditor(), "generate --check")
            {
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (Process process = Process.Start(check))
            {
                process.WaitForExit();
                Assert.AreEqual(0, process.ExitCode);
            }
        }

        [Test]
        public void EveryUserCodecGetsAnAdapterThatIsNotRegistered()
        {
            File.WriteAllText(Path.Combine(projectRoot, "Models", "Position.cs"), TwoCodecModel);

            FomoxaGenerateResult result = Generate();

            Assert.IsTrue(result.Succeeded, result.Error);
            string greeting = Read("Generated", "GreetingNetAdapter.cs");
            StringAssert.StartsWith(FomoxaGenerator.AdapterMarker + "\n", greeting);
            StringAssert.Contains("public sealed class GreetingNetAdapter : global::Fomoxa.Networking.Messaging.IMessageCodec<global::Sample.Models.Greeting>", greeting);
            StringAssert.Contains("public uint MessageId => GreetingNetCodec.MessageId;", greeting);
            StringAssert.Contains("IMessageCodec<global::Sample.Models.Position>", Read("Generated", "PositionNetAdapter.cs"));
            StringAssert.Contains("PositionWideCodec.Decode(ref reader, ref value);", Read("Generated", "PositionWideAdapter.cs"));
            StringAssert.Contains("namespace Generated\n{\n    [global::Fomoxa.Networking.MessageAdapter(\"net\")]\n    public sealed class GreetingNetAdapter", greeting);
            StringAssert.Contains("    [global::Fomoxa.Networking.MessageAdapter(\"net\")]\n    public sealed class PositionNetAdapter", Read("Generated", "PositionNetAdapter.cs"));
            StringAssert.Contains("    [global::Fomoxa.Networking.MessageAdapter(\"wide\")]\n    public sealed class PositionWideAdapter", Read("Generated", "PositionWideAdapter.cs"));
            string registration = Read("Generated", FomoxaGenerator.AdaptersFileName);
            StringAssert.DoesNotContain("GreetingNetAdapter", registration);
            StringAssert.DoesNotContain("PositionNetAdapter", registration);
            StringAssert.StartsWith(FomoxaGenerator.AdapterMarker + "\n", Read("Generated", "MessageBundleNetAdapter.cs"));
        }

        [Test]
        public void AdapterOfARemovedCodecIsRemoved()
        {
            File.WriteAllText(Path.Combine(projectRoot, "Models", "Position.cs"), TwoCodecModel);
            Generate();
            File.WriteAllText(Path.Combine(projectRoot, "Models", "Position.cs"), TwoCodecModel.Replace("        [Codec(\"wide\")]\n", "").Replace("    [Codec(\"wide\")]\n", ""));
            string handWritten = Path.Combine(projectRoot, "Generated", "NotesAdapter.cs");
            File.WriteAllText(handWritten, "// mine\n");

            FomoxaGenerateResult result = Generate();

            Assert.IsTrue(result.Succeeded, result.Error);
            Assert.IsTrue(result.Changed);
            Assert.IsFalse(File.Exists(Path.Combine(projectRoot, "Generated", "PositionWideAdapter.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(projectRoot, "Generated", "PositionNetAdapter.cs")));
            Assert.IsTrue(File.Exists(handWritten));
        }

        [Test]
        public void AdapterNameReplacesTheCodecSuffix()
        {
            Assert.AreEqual("GreetingNetAdapter", FomoxaGenerator.AdapterName("GreetingNetCodec"));
        }

        [Test]
        public void HandWrittenTomlIsNotOverwritten()
        {
            File.WriteAllText(Path.Combine(projectRoot, "fomoxa.toml"), "src = \"Models\"\n");

            FomoxaGenerateResult result = Generate();

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains("was not written by com.fomoxa.networking", result.Error);
            Assert.AreEqual("src = \"Models\"\n", Read("fomoxa.toml"));
        }

        [Test]
        public void ReliableChannelDeclarationReferencesTheMessageIdFomoxacGenerated()
        {
            FomoxaGenerateResult result = Generate(new ChannelDeclaration("Greeting", "net", Channel.ReliableOrdered));

            Assert.IsTrue(result.Succeeded, result.Error);
            StringAssert.Contains(
                "registry.Channels.Set(GreetingNetCodec.MessageId, global::Fomoxa.Networking.Channel.ReliableOrdered);",
                Read("Generated", FomoxaGenerator.AdaptersFileName));
        }

        [Test]
        public void ChannelDeclarationForAMessageFomoxacDidNotGenerateIsAnError()
        {
            FomoxaGenerateResult result = Generate(new ChannelDeclaration("Missing", "net", Channel.ReliableOrdered));

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains("did not generate a codec for Missing.net", result.Error);
        }

        [Test]
        public void HandWrittenFileInTheSystemModelsFolderIsNotOverwritten()
        {
            Directory.CreateDirectory(Path.Combine(projectRoot, FomoxaGenerator.SystemModelsFolder));
            File.WriteAllText(Path.Combine(projectRoot, FomoxaGenerator.SystemModelsFolder, "MessageBundle.cs"), "// mine\n");

            FomoxaGenerateResult result = Generate();

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains("was not written by com.fomoxa.networking", result.Error);
            Assert.AreEqual("// mine\n", Read(FomoxaGenerator.SystemModelsFolder, "MessageBundle.cs"));
        }

        [Test]
        public void CopiedSystemModelNoLongerInThePackageIsRemoved()
        {
            Generate();
            string stale = Path.Combine(projectRoot, FomoxaGenerator.SystemModelsFolder, "Removed.cs");
            File.WriteAllText(stale, FomoxaGenerator.SystemModelMarker + "\n");

            FomoxaGenerateResult result = Generate();

            Assert.IsTrue(result.Succeeded, result.Error);
            Assert.IsTrue(result.Changed);
            Assert.IsFalse(File.Exists(stale));
        }

        [Test]
        public void RpcModelIsWrittenAndRegisteredWithTheMessageIdFomoxacGenerated()
        {
            FomoxaGenerateResult result = GenerateRpcs(Jump, Wave);

            Assert.IsTrue(result.Succeeded, result.Error);
            string model = Read("Generated", FomoxaGenerator.RpcModelsFolder, "PlayerJump.cs");
            Assert.AreEqual(FomoxaGenerator.RpcModel(Jump), model);
            StringAssert.StartsWith(FomoxaGenerator.RpcModelMarker + "Game.Player.Jump. Edit the method, not this file.\n", model);
            StringAssert.Contains("namespace FomoxaRpcModels", model);
            StringAssert.Contains("    [Network]\n    [Codec(\"net\")]\n    [NetworkChannel(\"net\", Channel.ReliableOrdered)]\n    public sealed class PlayerJump\n", model);
            StringAssert.Contains("[NetworkChannel(\"net\", Channel.Unreliable)]", Read("Generated", FomoxaGenerator.RpcModelsFolder, "HandWave.cs"));
            StringAssert.Contains("public const string MessageName = \"PlayerJump.net\";", Read("Generated", "PlayerJumpNetCodec.cs"));
            StringAssert.Contains("IMessageCodec<global::FomoxaRpcModels.PlayerJump>", Read("Generated", "PlayerJumpNetAdapter.cs"));
            string registration = Read("Generated", FomoxaGenerator.AdaptersFileName);
            StringAssert.Contains("registry.Channels.Set(PlayerJumpNetCodec.MessageId, global::Fomoxa.Networking.Channel.ReliableOrdered);", registration);
            StringAssert.Contains("registry.Rpcs.Set(\"Game.Player\", \"Jump\", PlayerJumpNetCodec.MessageId);", registration);
            StringAssert.DoesNotContain("registry.Channels.Set(HandWaveNetCodec.MessageId", registration);
            StringAssert.Contains("registry.Rpcs.Set(\"Game.Player+Hand\", \"Wave\", HandWaveNetCodec.MessageId);", registration);
            CollectionAssert.IsEmpty(FomoxaGenerator.CheckRpcs(projectRoot, "Generated", new[] { Jump, Wave }));
        }

        [Test]
        public void SecondRunWithRpcsChangesNothingAndFomoxacCheckAgreesOutsideUnity()
        {
            GenerateRpcs(Jump);

            FomoxaGenerateResult second = GenerateRpcs(Jump);

            Assert.IsTrue(second.Succeeded, second.Error);
            Assert.IsFalse(second.Changed);
            var check = new ProcessStartInfo(FomoxacBinary.PathForThisEditor(), "generate --check")
            {
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (Process process = Process.Start(check))
            {
                process.WaitForExit();
                Assert.AreEqual(0, process.ExitCode);
            }
        }

        [Test]
        public void ModelOfARemovedRpcIsRemoved()
        {
            GenerateRpcs(Jump, Wave);
            string handWritten = Path.Combine(projectRoot, "Generated", FomoxaGenerator.RpcModelsFolder, "Notes.cs");
            File.WriteAllText(handWritten, "// mine\n");

            FomoxaGenerateResult result = GenerateRpcs(Wave);

            Assert.IsTrue(result.Succeeded, result.Error);
            Assert.IsTrue(result.Changed);
            Assert.IsFalse(File.Exists(Path.Combine(projectRoot, "Generated", FomoxaGenerator.RpcModelsFolder, "PlayerJump.cs")));
            Assert.IsFalse(File.Exists(Path.Combine(projectRoot, "Generated", "PlayerJumpNetCodec.cs")));
            Assert.IsFalse(File.Exists(Path.Combine(projectRoot, "Generated", "PlayerJumpNetAdapter.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(projectRoot, "Generated", FomoxaGenerator.RpcModelsFolder, "HandWave.cs")));
            StringAssert.DoesNotContain("PlayerJump", Read("Generated", FomoxaGenerator.AdaptersFileName));
            Assert.IsTrue(File.Exists(handWritten));
        }

        [Test]
        public void HandWrittenFileWithTheModelNameIsNotOverwritten()
        {
            Directory.CreateDirectory(Path.Combine(projectRoot, "Generated", FomoxaGenerator.RpcModelsFolder));
            File.WriteAllText(Path.Combine(projectRoot, "Generated", FomoxaGenerator.RpcModelsFolder, "PlayerJump.cs"), "// mine\n");

            FomoxaGenerateResult result = GenerateRpcs(Jump);

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains("was not written by com.fomoxa.networking", result.Error);
            Assert.AreEqual("// mine\n", Read("Generated", FomoxaGenerator.RpcModelsFolder, "PlayerJump.cs"));
        }

        [Test]
        public void RpcModelNamedLikeAUserModelIsAFomoxacError()
        {
            FomoxaGenerateResult result = GenerateRpcs(new RpcDeclaration("Game.Player", "Greet", "Greeting", Channel.ReliableOrdered));

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains("fomoxac generate exited with", result.Error);
            StringAssert.Contains("Greeting", result.Error);
        }

        [Test]
        public void CheckReportsMissingStaleAndUnregisteredRpcs()
        {
            IReadOnlyList<string> beforeGeneration = FomoxaGenerator.CheckRpcs(projectRoot, "Generated", new[] { Jump });
            GenerateRpcs(Jump);
            IReadOnlyList<string> channelChanged = FomoxaGenerator.CheckRpcs(projectRoot, "Generated", new[] { new RpcDeclaration("Game.Player", "Jump", "PlayerJump", Channel.Unreliable) });
            string adaptersPath = Path.Combine(projectRoot, "Generated", FomoxaGenerator.AdaptersFileName);
            File.WriteAllText(adaptersPath, File.ReadAllText(adaptersPath).Replace("registry.Rpcs.Set", "// registry.Rpcs.Set"));
            IReadOnlyList<string> unregistered = FomoxaGenerator.CheckRpcs(projectRoot, "Generated", new[] { Jump });

            StringAssert.Contains("[NetworkRpc] Game.Player.Jump: its model PlayerJump", beforeGeneration[0]);
            StringAssert.Contains("is missing or out of date", channelChanged[0]);
            StringAssert.Contains("does not register it", unregistered[0]);
            StringAssert.Contains("Tools > Fomoxa > Generate", unregistered[1]);
        }

        [Test]
        public void FomoxacErrorIsReported()
        {
            File.WriteAllText(Path.Combine(projectRoot, "Models", "GreetingAgain.cs"), GreetingModel.Replace("Sample.Models", "Sample.Other"));

            FomoxaGenerateResult result = Generate();

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains("fomoxac generate exited with", result.Error);
        }
    }
}
