using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Fomoxa.Unity.Editor;
using NUnit.Framework;

namespace Fomoxa.Unity.Tests
{
    public sealed class FomoxaUnityCodecsTest
    {
        private const string PoseModel =
            "using Fomoxa;\n"
            + "using UnityEngine;\n"
            + "\n"
            + "namespace Sample.Models\n"
            + "{\n"
            + "    [Network]\n"
            + "    [Codec(\"net\")]\n"
            + "    [Codec(\"wide\")]\n"
            + "    public class Pose\n"
            + "    {\n"
            + "        [Network(\"Vector3\")]\n"
            + "        [Codec(\"net\")]\n"
            + "        [Codec(\"wide\")]\n"
            + "        public Vector3 Position { get; set; }\n"
            + "\n"
            + "        [Network(\"Array<Color32>\")]\n"
            + "        [Codec(\"net\")]\n"
            + "        public Color32[] Tints { get; set; }\n"
            + "    }\n"
            + "}\n";

        private string projectRoot;

        private string UnityCodecsFolder => Path.Combine(projectRoot, "Generated", FomoxaUnityCodecs.Folder);

        private string InspectSchema => Path.Combine(projectRoot, FomoxaUnityCodecs.InspectSchemaPath);

        [SetUp]
        public void CreateProject()
        {
            projectRoot = Path.GetFullPath(Path.Combine("Temp", "FomoxaUnityCodecsTest", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(Path.Combine(projectRoot, "Models"));
            File.WriteAllText(Path.Combine(projectRoot, "Models", "Pose.cs"), PoseModel);
        }

        [TearDown]
        public void DeleteProject()
        {
            Directory.Delete(projectRoot, true);
        }

        [Test]
        public void EveryUnityTypeAndCodecPairGetsACodecAndTheInspectSchemaDescribesThem()
        {
            FomoxaGenerateResult result = Generate();

            Assert.IsTrue(result.Succeeded, result.Error);
            StringAssert.Contains("the field type Array<Color32> (codec net) is an array of a Unity type", result.Warnings);
            CollectionAssert.AreEquivalent(
                new[] { "Color32NetCodec.cs", "Vector3NetCodec.cs", "Vector3WideCodec.cs" },
                Array.ConvertAll(Directory.GetFiles(UnityCodecsFolder, "*.cs"), Path.GetFileName));
            string vector = File.ReadAllText(Path.Combine(UnityCodecsFolder, "Vector3NetCodec.cs"));
            StringAssert.StartsWith(FomoxaUnityCodecs.Marker + "\nnamespace Generated\n{\n    public static class Vector3NetCodec\n", vector);
            StringAssert.Contains("        public static void Encode(Writer writer, global::UnityEngine.Vector3 value)\n        {\n            writer.WriteF32(value.x);\n            writer.WriteF32(value.y);\n            writer.WriteF32(value.z);\n        }\n", vector);
            StringAssert.Contains("            value.z = reader.FieldAbsent() ? 0f : reader.ReadF32();\n", vector);
            StringAssert.Contains("            value.a = reader.FieldAbsent() ? (byte)0 : reader.ReadU8();\n", File.ReadAllText(Path.Combine(UnityCodecsFolder, "Color32NetCodec.cs")));

            Dictionary<string, object> models = FomoxaJson.AsObject(FomoxaJson.AsObject(FomoxaJson.Parse(File.ReadAllText(InspectSchema)))["models"]);
            Dictionary<string, object> vectorModel = FomoxaJson.AsObject(models["Vector3"]);
            Assert.AreEqual(FomoxaUnityCodecs.InspectSource, vectorModel["source"]);
            CollectionAssert.AreEqual(new object[] { "net", "wide" }, FomoxaJson.AsArray(vectorModel["codecs"]));
            List<object> fields = FomoxaJson.AsArray(FomoxaJson.AsObject(FomoxaJson.AsObject(vectorModel["messages"])["wide"])["fields"]);
            Assert.AreEqual("z", FomoxaJson.AsObject(fields[2])["name"]);
            Assert.AreEqual("f32", FomoxaJson.AsObject(fields[2])["type"]);
            CollectionAssert.AreEqual(new object[] { "net" }, FomoxaJson.AsArray(FomoxaJson.AsObject(models["Color32"])["codecs"]));
            Assert.IsTrue(models.ContainsKey("Pose"));
            StringAssert.DoesNotContain(FomoxaUnityCodecs.InspectSource, File.ReadAllText(Path.Combine(projectRoot, FomoxaUnityCodecs.SchemaPath)));
        }

        [Test]
        public void ASecondRunChangesNothingAndFomoxacCheckAgrees()
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
        public void CodecsOfRemovedFieldsAndTheInspectSchemaGoAwayButHandWrittenFilesStay()
        {
            Generate();
            string handWritten = Path.Combine(UnityCodecsFolder, "Notes.cs");
            File.WriteAllText(handWritten, "// mine\n");
            File.WriteAllText(Path.Combine(projectRoot, "Models", "Pose.cs"), PoseModel
                .Replace("        [Network(\"Vector3\")]\n        [Codec(\"net\")]\n        [Codec(\"wide\")]\n        public Vector3 Position { get; set; }\n\n", "")
                .Replace("        [Network(\"Array<Color32>\")]", "        [Network(\"u32\")]")
                .Replace("public Color32[] Tints", "public uint Tints"));

            FomoxaGenerateResult result = Generate();

            Assert.IsTrue(result.Succeeded, result.Error);
            Assert.IsTrue(result.Changed);
            CollectionAssert.AreEqual(new[] { handWritten }, Directory.GetFiles(UnityCodecsFolder, "*.cs"));
            Assert.IsFalse(File.Exists(InspectSchema));
        }

        [Test]
        public void AHandWrittenFileWithTheCodecNameIsNotOverwritten()
        {
            Directory.CreateDirectory(UnityCodecsFolder);
            File.WriteAllText(Path.Combine(UnityCodecsFolder, "Vector3NetCodec.cs"), "// mine\n");

            FomoxaGenerateResult result = Generate();

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains("Vector3NetCodec.cs was not written by com.fomoxa.networking", result.Error);
            Assert.AreEqual("// mine\n", File.ReadAllText(Path.Combine(UnityCodecsFolder, "Vector3NetCodec.cs")));
        }

        [Test]
        public void AnUnknownTypeIsAWarningAndARegisteredTypeGetsACodec()
        {
            File.WriteAllText(Path.Combine(projectRoot, "Models", "Pose.cs"), PoseModel
                .Replace("\"Array<Color32>\"", "\"Bounds\"").Replace("Color32[] Tints", "UnityEngine.Bounds Tints")
                .Replace("\"Vector3\"", "\"GridCell\"").Replace("Vector3 Position", "Sample.GridCell Position"));
            FomoxaUnityTypes.Register("GridCell", "Sample.GridCell", new FomoxaUnityField("Column", "u16"), new FomoxaUnityField("Row", "u16"));

            FomoxaGenerateResult result = Generate();

            Assert.IsTrue(result.Succeeded, result.Error);
            StringAssert.Contains("the field type Bounds (codec net) is neither a model nor a Unity type", result.Warnings);
            StringAssert.Contains("FomoxaUnityTypes.Register", result.Warnings);
            string cell = File.ReadAllText(Path.Combine(UnityCodecsFolder, "GridCellWideCodec.cs"));
            StringAssert.Contains("writer.WriteU16(value.Column);", cell);
            StringAssert.Contains("value.Row = reader.FieldAbsent() ? (ushort)0 : reader.ReadU16();", cell);
            Assert.IsFalse(File.Exists(Path.Combine(UnityCodecsFolder, "BoundsNetCodec.cs")));
            Assert.Throws<ArgumentException>(() => new FomoxaUnityField("x", "string"));
        }

        [Test]
        public void JsonKeepsOrderNumbersAndEscapes()
        {
            const string json = "{\n  \"b\": 1.5e3,\n  \"a\": [\n    true,\n    null,\n    \"q\\\"\\u0001\"\n  ],\n  \"e\": {}\n}\n";

            Assert.AreEqual(json, FomoxaJson.Write(FomoxaJson.Parse(json)));
            Assert.AreEqual("q\"\u0001", FomoxaJson.AsArray(FomoxaJson.AsObject(FomoxaJson.Parse(json))["a"])[2]);
            Assert.Throws<FormatException>(() => FomoxaJson.Parse("{\"a\": 1} x"));
            Assert.Throws<FormatException>(() => FomoxaJson.Parse("[1,"));
        }

        [Test]
        public void CodecNamesFollowFomoxacPascalCase()
        {
            Assert.AreEqual("Vector3EdgeV2Codec", FomoxaUnityCodecs.CodecClass("Vector3", "edge_v2"));
        }

        private FomoxaGenerateResult Generate() =>
            FomoxaGenerator.Run(
                projectRoot,
                "Models",
                "Generated",
                FomoxacBinary.PathForThisEditor(),
                FomoxaSystemModels.SourcePathForThisEditor(),
                new ChannelDeclaration[0],
                new RpcDeclaration[0]);
    }
}
