using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.TCE;
using ZeroEngine.TCE.ModSystem;

namespace ZeroEngine.TCE.ModSystem.Tests.Editor
{
    [TestFixture]
    public sealed class TceModGraphImporterTests
    {
        private const string SampleGraphPath = "Packages/com.zerogamestudio.zeroengine.tce/Samples~/ModGraphImport/content/graphs/burning_hit.tce.json";
        private string tempRoot;

        [SetUp]
        public void SetUp()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "ZeroEngineTceModBridgeTests", TestContext.CurrentContext.Test.ID);
            Directory.CreateDirectory(tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }

        [Test]
        public void TryParse_SampleGraph_ReturnsExternalDocument()
        {
            string json = File.ReadAllText(SampleGraphPath);

            bool parsed = TceModGraphJsonParser.TryParse(json, SampleGraphPath, out TceExternalGraphDocument document, out var issues);

            Assert.IsTrue(parsed, FormatIssues(issues));
            Assert.AreEqual(TceGraphSchema.Format, document.Format);
            Assert.AreEqual(TceGraphSchema.CurrentVersion, document.SchemaVersion);
            Assert.AreEqual("burning_hit", document.GraphId);
            Assert.AreEqual("zeroengine.tce.trigger.on_install", document.Triggers[0].ComponentId);
            Assert.AreEqual("zeroengine.tce.effect.debug_log", document.Effects[0].ComponentId);
            Assert.AreEqual("Burning Hit accepted.", document.Effects[0].Fields["Message"]);
        }

        [Test]
        public void TryParse_TypeHints_ReturnsValidationIssue()
        {
            const string json = "{\"format\":\"zeroengine-tce-graph\",\"schemaVersion\":1,\"graphId\":\"bad\",\"displayName\":\"Bad\",\"triggers\":[{\"componentId\":\"zeroengine.tce.trigger.on_install\",\"$type\":\"ZeroEngine.TCE.OnInstallTriggerData\",\"fields\":{}}],\"conditions\":[],\"effects\":[]}";

            bool parsed = TceModGraphJsonParser.TryParse(json, "bad.tce.json", out _, out var issues);

            Assert.IsFalse(parsed);
            Assert.That(issues.Any(issue => issue.Code == TceValidationCodes.InvalidField));
        }

        [Test]
        public void TryParse_ManagedReferenceTypeName_ReturnsValidationIssue()
        {
            const string json = "{\"format\":\"zeroengine-tce-graph\",\"schemaVersion\":1,\"graphId\":\"bad\",\"displayName\":\"Bad\",\"triggers\":[],\"conditions\":[],\"effects\":[],\"managedReferenceFullTypename\":\"Assembly-CSharp SomeType\"}";

            bool parsed = TceModGraphJsonParser.TryParse(json, "bad.tce.json", out _, out var issues);

            Assert.IsFalse(parsed);
            Assert.That(issues.Any(issue => issue.Code == TceValidationCodes.InvalidField));
        }

        [Test]
        public void Import_ValidManifestGraph_RegistersGraph()
        {
            WriteGraph("content/graphs/burning_hit.tce.json", File.ReadAllText(SampleGraphPath));
            var manifest = new TceModGraphImportManifest(
                "sample.mod",
                tempRoot,
                new[] { "content/graphs/burning_hit.tce.json" });

            TceModGraphImportBatchResult result = TceModGraphImporter.Import(
                manifest,
                TceComponentRegistry.CreateDefault());

            Assert.AreEqual(1, result.Results.Count);
            Assert.IsTrue(result.Results[0].Succeeded, FormatIssues(result.Results[0].Issues));
            Assert.IsTrue(result.Registry.TryGet("burning_hit", out _));
        }

        [Test]
        public void Import_InvalidGraph_DoesNotBlockValidGraph()
        {
            WriteGraph("content/graphs/valid.tce.json", BuildGraphJson("valid_graph", "zeroengine.tce.effect.debug_log"));
            WriteGraph("content/graphs/invalid.tce.json", BuildGraphJson("invalid_graph", "not.allowed"));
            var manifest = new TceModGraphImportManifest(
                "sample.mod",
                tempRoot,
                new[] { "content/graphs/invalid.tce.json", "content/graphs/valid.tce.json" });

            TceModGraphImportBatchResult result = TceModGraphImporter.Import(
                manifest,
                TceComponentRegistry.CreateDefault());

            Assert.AreEqual(2, result.Results.Count);
            Assert.That(result.Results.Count(item => item.Succeeded), Is.EqualTo(1));
            Assert.IsTrue(result.Registry.TryGet("valid_graph", out _));
            Assert.IsFalse(result.Registry.TryGet("invalid_graph", out _));
        }

        [Test]
        public void Import_AbsoluteGraphPath_ReturnsPathIssue()
        {
            var manifest = new TceModGraphImportManifest(
                "sample.mod",
                tempRoot,
                new[] { Path.GetFullPath(Path.Combine(tempRoot, "content/graphs/burning_hit.tce.json")) });

            TceModGraphImportBatchResult result = TceModGraphImporter.Import(
                manifest,
                TceComponentRegistry.CreateDefault());

            Assert.AreEqual(1, result.Results.Count);
            Assert.IsFalse(result.Results[0].Succeeded);
            Assert.That(result.Results[0].Issues.Any(issue => issue.Message.Contains("relative")));
        }

        [Test]
        public void Import_TraversalGraphPath_ReturnsPathIssue()
        {
            var manifest = new TceModGraphImportManifest(
                "sample.mod",
                tempRoot,
                new[] { "../escape.tce.json" });

            TceModGraphImportBatchResult result = TceModGraphImporter.Import(
                manifest,
                TceComponentRegistry.CreateDefault());

            Assert.AreEqual(1, result.Results.Count);
            Assert.IsFalse(result.Results[0].Succeeded);
            Assert.That(result.Results[0].Issues.Any(issue => issue.Message.Contains("mod root")));
        }

        [Test]
        public void BridgeAssembly_DependsOnStandaloneModSystemNotOldContracts()
        {
            string asmdefPath = Path.Combine(
                Application.dataPath,
                "../Packages/com.zerogamestudio.zeroengine.tce.modsystem/Runtime/ZeroEngine.TCE.ModSystem.asmdef");
            string asmdef = File.ReadAllText(asmdefPath);

            StringAssert.Contains("\"ZeroEngine.ModSystem\"", asmdef);
            StringAssert.DoesNotContain("ZeroEngine.ModSystem.Contracts", asmdef);
            StringAssert.DoesNotContain("\"ZeroEngine\",", asmdef);
        }

        [Test]
        public void BridgePackage_DependsOnStandaloneModSystemPackage()
        {
            string packageJsonPath = Path.Combine(
                Application.dataPath,
                "../Packages/com.zerogamestudio.zeroengine.tce.modsystem/package.json");
            string packageJson = File.ReadAllText(packageJsonPath);

            StringAssert.Contains("\"com.zerogamestudio.zeroengine.modsystem\"", packageJson);
            StringAssert.DoesNotContain("\"com.zerogamestudio.zeroengine\":", packageJson);
        }

        private static string FormatIssues(System.Collections.Generic.IReadOnlyList<TceValidationIssue> issues)
        {
            return string.Join("\n", issues.Select(issue => $"{issue.Code} {issue.Path}: {issue.Message}"));
        }

        private void WriteGraph(string relativePath, string json)
        {
            string path = Path.Combine(tempRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);
        }

        private static string BuildGraphJson(string graphId, string effectComponentId)
        {
            return "{"
                + "\"format\":\"zeroengine-tce-graph\","
                + "\"schemaVersion\":1,"
                + $"\"graphId\":\"{graphId}\","
                + $"\"displayName\":\"{graphId}\","
                + "\"triggers\":[{\"componentId\":\"zeroengine.tce.trigger.on_install\",\"fields\":{}}],"
                + "\"conditions\":[],"
                + $"\"effects\":[{{\"componentId\":\"{effectComponentId}\",\"fields\":{{\"Message\":\"accepted\"}}}}]"
                + "}";
        }
    }
}
