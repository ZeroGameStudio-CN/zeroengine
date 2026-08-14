using System;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using NUnit.Framework;
using ZeroGameStudio.ConfigPipeline.Editor;

namespace ZeroGameStudio.ConfigPipeline.Tests.Editor
{
    [TestFixture]
    [Category("ZGS.ConfigPipeline.CoreContract")]
    public sealed class ProjectPipelineTests
    {
        private string root;
        private ConfigSchema schema;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "zgs-config-project-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "Config"));
            byte[] schemaBytes = Utf8(
                "{\"$id\":\"urn:zgs:test:project\",\"x-zgs-schema-version\":1," +
                "\"type\":\"object\",\"additionalProperties\":false," +
                "\"required\":[\"items\",\"groups\"],\"properties\":{" +
                "\"items\":{\"type\":\"array\",\"x-zgs-sheet\":\"Items\",\"items\":{" +
                "\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"id\",\"value\"]," +
                "\"properties\":{\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true}," +
                "\"value\":{\"type\":\"integer\",\"x-zgs-number-type\":\"int32\"}," +
                "\"clientValue\":{\"type\":\"string\",\"x-zgs-scope\":\"client\"}," +
                "\"serverValue\":{\"type\":\"string\",\"x-zgs-scope\":\"server\"}}}}," +
                "\"groups\":{\"type\":\"array\",\"x-zgs-sheet\":\"Groups\",\"items\":{" +
                "\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"id\"]," +
                "\"properties\":{\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true}}}}}}");
            File.WriteAllBytes(Path.Combine(root, "Config", "schema.json"), schemaBytes);
            schema = ConfigSchemaParser.Parse(schemaBytes);
            WriteWorkbook("Config/items.xlsx", "items", "item-a", 7);
            WriteWorkbook("Config/groups.xlsx", "groups", "group-a", null);
            File.WriteAllBytes(Path.Combine(root, "Config", "config-project.json"), Utf8(ProfileJson()));
        }

        [TearDown]
        public void TearDown()
        {
            ConfigMaintenanceRegistry.ClearForTests();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void Profile_RejectsDuplicateTableOwners()
        {
            string json = ProfileJson().Replace(
                "[\"groups\"]",
                "[\"items\"]");

            Assert.Throws<InvalidDataException>(() => ConfigProjectProfileParser.Parse(Utf8(json)));
        }

        [Test]
        public void PlanApplyCheck_MergesOwnedWorkbooksAndIsDeterministic()
        {
            var service = new ConfigPipelineService();

            ConfigPipelinePreparedPlan plan = service.Plan(
                root,
                "Config/config-project.json",
                "sample",
                "package@1");
            Assert.That(plan.Plan.IsCurrent, Is.False);
            Assert.That(File.Exists(Path.Combine(root, "Generated", "sample.json")), Is.False);

            service.Apply(root, "Config/config-project.json", "sample", "package@1");

            Assert.That(service.Check(root, "Config/config-project.json", "sample", "package@1"), Is.True);
            Assert.That(service.Check(root, "Config/config-project.json", "sample", "package@2"), Is.False);
            string json = File.ReadAllText(Path.Combine(root, "Generated", "sample.json"));
            Assert.That(json, Does.Contain("\"item-a\""));
            Assert.That(json, Does.Contain("\"group-a\""));
        }

        [Test]
        public void ExportJsonCandidate_NeverOverwritesOfficialWorkbooks()
        {
            var service = new ConfigPipelineService();
            service.Apply(root, "Config/config-project.json", "sample", "package@1");
            string officialHash = ConfigPipelinePlanBuilder.HashFile(Path.Combine(root, "Config", "items.xlsx"));
            string candidates = Path.Combine(root, "Candidates");

            ConfigImportConflictResult result = service.ExportJsonCandidate(
                root,
                "Config/config-project.json",
                "sample",
                "client",
                candidates);

            Assert.That(result.Decision, Is.EqualTo(ConfigImportDecision.CandidateCurrentEqual));
            Assert.That(File.Exists(Path.Combine(candidates, "items.candidate.xlsx")), Is.True);
            Assert.That(File.Exists(Path.Combine(candidates, "groups.candidate.xlsx")), Is.True);
            Assert.That(
                ConfigPipelinePlanBuilder.HashFile(Path.Combine(root, "Config", "items.xlsx")),
                Is.EqualTo(officialHash));
        }

        [Test]
        public void RefreshCandidate_PreservesAllWorkbookDataAndOfficialFiles()
        {
            var service = new ConfigPipelineService();
            string itemsHash = ConfigPipelinePlanBuilder.HashFile(Path.Combine(root, "Config", "items.xlsx"));
            string groupsHash = ConfigPipelinePlanBuilder.HashFile(Path.Combine(root, "Config", "groups.xlsx"));
            string candidates = Path.Combine(root, "RefreshCandidates");

            ConfigWorkbookRefreshCandidateResult result = service.ExportWorkbookRefreshCandidate(
                root,
                "Config/config-project.json",
                "sample",
                candidates);

            Assert.That(result.CandidateFileCount, Is.EqualTo(2));
            Assert.That(result.SourceHash, Is.Not.Empty);
            Assert.That(
                ConfigPipelinePlanBuilder.HashFile(Path.Combine(root, "Config", "items.xlsx")),
                Is.EqualTo(itemsHash));
            Assert.That(
                ConfigPipelinePlanBuilder.HashFile(Path.Combine(root, "Config", "groups.xlsx")),
                Is.EqualTo(groupsHash));
            foreach (string name in new[] { "items.candidate.xlsx", "groups.candidate.xlsx" })
            {
                string path = Path.Combine(candidates, name);
                Assert.That(File.Exists(path), Is.True);
                using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(path, false))
                {
                    Assert.That(new OpenXmlValidator().Validate(workbook), Is.Empty);
                    Assert.That(
                        workbook.WorkbookPart.Workbook.Sheets
                            .Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>()
                            .Any(value => value.Name.Value == XlsxConfigWorkbookWriter.NavigationSheetName),
                        Is.True);
                }
            }

            ConfigPipelineCommandResult batch = ConfigPipelineBatch.Run(
                root,
                "Config/config-project.json",
                "sample",
                null,
                ConfigPipelineMode.RefreshCandidate,
                Path.Combine(root, "BatchRefreshCandidates"));
            Assert.That(batch.Success, Is.True);
            Assert.That(batch.Summary, Does.Contain(result.SourceHash));
            Assert.That(
                ConfigPipelineBatch.RequiresPackageIdentity(ConfigPipelineMode.RefreshCandidate),
                Is.False);
        }

        [Test]
        public void RefreshCandidate_RejectsNonEmptyOutputWithoutChangingIt()
        {
            string candidates = Path.Combine(root, "ExistingCandidates");
            Directory.CreateDirectory(candidates);
            string marker = Path.Combine(candidates, "keep.txt");
            File.WriteAllText(marker, "keep", new UTF8Encoding(false));

            Assert.Throws<InvalidOperationException>(() =>
                new ConfigPipelineService().ExportWorkbookRefreshCandidate(
                    root,
                    "Config/config-project.json",
                    "sample",
                    candidates));

            Assert.That(File.ReadAllText(marker), Is.EqualTo("keep"));
            Assert.That(Directory.GetFiles(candidates), Is.EqualTo(new[] { marker }));
        }

        [Test]
        public void BatchUpgradeCandidate_PreservesCurrentWorkbookData()
        {
            var service = new ConfigPipelineService();
            service.Apply(root, "Config/config-project.json", "sample", "package@1");
            string officialHash = ConfigPipelinePlanBuilder.HashFile(
                Path.Combine(root, "Config", "items.xlsx"));
            string nextSchemaJson = File.ReadAllText(Path.Combine(root, "Config", "schema.json"))
                .Replace("\"x-zgs-schema-version\":1", "\"x-zgs-schema-version\":2")
                .Replace(
                    "\"serverValue\":{\"type\":\"string\",\"x-zgs-scope\":\"server\"}",
                    "\"serverValue\":{\"type\":\"string\",\"x-zgs-scope\":\"server\"}," +
                    "\"descriptionKey\":{\"type\":\"string\"}");
            File.WriteAllText(
                Path.Combine(root, "Config", "schema-v2.json"),
                nextSchemaJson,
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(root, "Config", "config-project-v2.json"),
                ProfileJson().Replace("Config/schema.json", "Config/schema-v2.json"),
                new UTF8Encoding(false));
            string candidates = Path.Combine(root, "UpgradeCandidates");

            ConfigPipelineCommandResult result = ConfigPipelineBatch.Run(
                root,
                "Config/config-project.json",
                "sample",
                "package@1",
                ConfigPipelineMode.UpgradeCandidate,
                candidates,
                null,
                "Config/config-project-v2.json");

            Assert.That(result.Success, Is.True);
            Assert.That(result.Summary, Does.Contain("1->2:2"));
            ConfigSchema nextSchema = ConfigSchemaParser.Parse(Utf8(nextSchemaJson));
            using (FileStream stream = File.OpenRead(Path.Combine(candidates, "items.candidate.xlsx")))
            {
                ConfigDocument candidate = new XlsxConfigSourceReader(
                    nextSchema,
                    null,
                    new[] { "items" }).Read(
                        stream,
                        new ConfigReadContext("sample", nextSchema.SchemaId, nextSchema.SchemaVersion));
                Assert.That(CanonicalJsonWriter.WriteText(candidate.Root), Does.Contain("\"item-a\""));
            }

            Assert.That(
                ConfigPipelinePlanBuilder.HashFile(Path.Combine(root, "Config", "items.xlsx")),
                Is.EqualTo(officialHash));
        }

        [Test]
        public void BatchApi_CheckIsReadOnlyAndReturnsMachineResult()
        {
            Assert.That(
                ConfigPipelineBatch.RequiresPackageIdentity(ConfigPipelineMode.ExportCandidate),
                Is.False);
            Assert.That(
                ConfigPipelineBatch.RequiresPackageIdentity(ConfigPipelineMode.UpgradeCandidate),
                Is.True);
            ConfigPipelineCommandResult stale = ConfigPipelineBatch.Run(
                root,
                "Config/config-project.json",
                "sample",
                "package@1",
                ConfigPipelineMode.Check);

            Assert.That(stale.Success, Is.False);
            Assert.That(File.Exists(Path.Combine(root, "Generated", "sample.json")), Is.False);
            Assert.That(Encoding.UTF8.GetString(stale.MachineJson), Does.Contain("\"current\": false"));
            string resultPath = Path.Combine(root, "BatchResults", "result.json");
            ConfigPipelineBatch.WriteMachineResult(resultPath, stale.MachineJson);
            Assert.That(File.ReadAllBytes(resultPath), Is.EqualTo(stale.MachineJson));
            byte[] failure = ConfigPipelineBatch.CreateFailureMachineJson(
                new InvalidOperationException("synthetic failure"));
            ConfigPipelineBatch.WriteMachineResult(resultPath, failure);
            string failureJson = File.ReadAllText(resultPath);
            Assert.That(failureJson, Does.Contain("\"success\": false"));
            Assert.That(failureJson, Does.Contain("synthetic failure"));
        }

        [Test]
        public void Apply_CreatesDeterministicMetaForNewUnityArtifacts()
        {
            File.WriteAllText(
                Path.Combine(root, "Config", "config-project.json"),
                ProfileJson().Replace("Generated/", "Assets/Generated/"),
                new UTF8Encoding(false));
            var service = new ConfigPipelineService();
            ConfigPipelinePreparedPlan plan = service.Plan(
                root,
                "Config/config-project.json",
                "sample",
                "package@1");

            Assert.That(plan.Plan.Entries.Any(value => value.RelativePath.EndsWith(".meta", StringComparison.Ordinal)), Is.True);
            Assert.That(
                plan.Plan.Entries.Any(value => value.RelativePath == "Assets/Generated.meta"),
                Is.True);
            var otherArtifacts = new System.Collections.Generic.List<ConfigArtifact>
            {
                new ConfigArtifact("Assets/Generated/other.json", Utf8("{}"))
            };
            ConfigPipelineService.AddRequiredUnityMetas(root, "other", otherArtifacts);
            byte[] plannedDirectoryMeta = plan.Artifacts.Single(
                value => value.RelativePath == "Assets/Generated.meta").Content;
            byte[] otherDirectoryMeta = otherArtifacts.Single(
                value => value.RelativePath == "Assets/Generated.meta").Content;
            Assert.That(otherDirectoryMeta, Is.EqualTo(plannedDirectoryMeta));
            service.Apply(root, "Config/config-project.json", "sample", "package@1");
            string directoryMeta = File.ReadAllText(Path.Combine(root, "Assets", "Generated.meta"));
            string meta = File.ReadAllText(Path.Combine(root, "Assets", "Generated", "sample.json.meta"));
            Assert.That(directoryMeta, Does.Contain("folderAsset: yes"));
            Assert.That(directoryMeta, Does.Match("guid: [0-9a-f]{32}"));
            Assert.That(meta, Does.Match("guid: [0-9a-f]{32}"));
            Assert.That(service.Check(root, "Config/config-project.json", "sample", "package@1"), Is.True);
        }

        [Test]
        public void Plan_ReportsStableIdFieldDiff()
        {
            var service = new ConfigPipelineService();
            service.Apply(root, "Config/config-project.json", "sample", "package@1");
            WriteWorkbook("Config/items.xlsx", "items", "item-a", 8);

            ConfigPipelinePreparedPlan plan = service.Plan(
                root,
                "Config/config-project.json",
                "sample",
                "package@1");

            Assert.That(
                plan.ValueDiffs.Any(value => value.FieldPath.Contains("[id=item-a]/value")),
                Is.True);
        }

        [Test]
        public void CatalogEditor_UsesSamePlanAndTransactionalApplyRoute()
        {
            string input = Path.Combine(root, "Catalog", "input.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(input));
            File.WriteAllText(input, "catalog-source", new UTF8Encoding(false));
            ConfigMaintenanceRegistry.RegisterCatalogEditor("sample", new FakeCatalogEditor());
            var service = new ConfigCatalogMaintenanceService();
            var bindings = new[]
            {
                new ConfigAssetBinding("icon.coin", "0123456789abcdef0123456789abcdef", "Sprite")
            };

            ConfigCatalogPreparedPlan plan = service.Plan(root, "sample", "package@1", bindings);
            Assert.That(plan.Plan.IsCurrent, Is.False);
            Assert.That(File.Exists(Path.Combine(root, "Catalog", "catalog.json")), Is.False);

            service.Apply(root, "sample", "package@1", bindings);
            Assert.That(service.Plan(root, "sample", "package@1", bindings).Plan.IsCurrent, Is.True);
        }

        [Test]
        public void CatalogEditor_CreatesDeterministicMetaForNewUnityArtifact()
        {
            string input = Path.Combine(root, "Catalog", "input.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(input));
            File.WriteAllText(input, "catalog-source", new UTF8Encoding(false));
            ConfigMaintenanceRegistry.RegisterCatalogEditor(
                "sample",
                new FakeCatalogEditor("Assets/Generated/catalog.json"));
            var service = new ConfigCatalogMaintenanceService();
            var bindings = new[]
            {
                new ConfigAssetBinding("icon.coin", "0123456789abcdef0123456789abcdef", "Sprite")
            };

            ConfigCatalogPreparedPlan plan = service.Plan(root, "sample", "package@1", bindings);
            Assert.That(
                plan.Plan.Entries.Any(value => value.RelativePath == "Assets/Generated/catalog.json.meta"),
                Is.True);
            service.Apply(root, "sample", "package@1", bindings);
            string meta = File.ReadAllText(Path.Combine(
                root,
                "Assets",
                "Generated",
                "catalog.json.meta"));
            Assert.That(meta, Does.Match("guid: [0-9a-f]{32}"));
            Assert.That(service.Plan(root, "sample", "package@1", bindings).Plan.IsCurrent, Is.True);
        }

        [Test]
        public void ExportClientCandidate_PreservesServerOnlyWorkbookFields()
        {
            WriteScopedItemsWorkbook();
            var service = new ConfigPipelineService();
            service.Apply(root, "Config/config-project.json", "sample", "package@1");
            string candidates = Path.Combine(root, "ScopedCandidates");

            service.ExportJsonCandidate(
                root,
                "Config/config-project.json",
                "sample",
                "client",
                candidates);

            using (FileStream stream = File.OpenRead(Path.Combine(candidates, "items.candidate.xlsx")))
            {
                ConfigDocument candidate = new XlsxConfigSourceReader(
                    schema,
                    null,
                    new[] { "items" }).Read(
                        stream,
                        new ConfigReadContext("sample", schema.SchemaId, schema.SchemaVersion));
                string json = CanonicalJsonWriter.WriteText(candidate.Root);
                Assert.That(json, Does.Contain("\"serverValue\": \"server-kept\""));
            }
        }

        private void WriteWorkbook(string relativePath, string property, string id, int? value)
        {
            var fields = new System.Collections.Generic.List<ConfigProperty>
            {
                new ConfigProperty("id", new ConfigStringNode(id))
            };
            if (value.HasValue)
            {
                fields.Add(new ConfigProperty("value", new ConfigIntegerNode(value.Value)));
            }

            var rootNode = new ConfigObjectNode(new[]
            {
                new ConfigProperty(property, new ConfigArrayNode(new ConfigNode[]
                {
                    new ConfigObjectNode(fields)
                }))
            });
            string absolute = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            using (FileStream stream = File.Create(absolute))
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(
                    stream,
                    schema,
                    "sample",
                    new ConfigDocument("sample", schema.SchemaId, schema.SchemaVersion, rootNode),
                    null,
                    new[] { property });
            }
        }

        private void WriteScopedItemsWorkbook()
        {
            var item = new ConfigObjectNode(new[]
            {
                new ConfigProperty("id", new ConfigStringNode("item-a")),
                new ConfigProperty("value", new ConfigIntegerNode(7)),
                new ConfigProperty("clientValue", new ConfigStringNode("client-value")),
                new ConfigProperty("serverValue", new ConfigStringNode("server-kept"))
            });
            string absolute = Path.Combine(root, "Config", "items.xlsx");
            using (FileStream stream = File.Create(absolute))
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(
                    stream,
                    schema,
                    "sample",
                    new ConfigDocument(
                        "sample",
                        schema.SchemaId,
                        schema.SchemaVersion,
                        new ConfigObjectNode(new[]
                        {
                            new ConfigProperty("items", new ConfigArrayNode(new ConfigNode[] { item }))
                        })),
                    null,
                    new[] { "items" });
            }
        }

        private static string ProfileJson()
        {
            return "{\"formatVersion\":1,\"configSets\":[{" +
                   "\"configSetId\":\"sample\",\"authoringSource\":\"excel\"," +
                   "\"schema\":\"Config/schema.json\",\"workbooks\":[" +
                   "{\"path\":\"Config/items.xlsx\",\"tables\":[\"items\"]}," +
                   "{\"path\":\"Config/groups.xlsx\",\"tables\":[\"groups\"]}]," +
                   "\"generatedNamespace\":\"Sample.Generated\",\"rootClassName\":\"SampleConfig\"," +
                   "\"codePath\":\"Generated/SampleConfig.g.cs\",\"targets\":[{" +
                   "\"scope\":\"client\",\"json\":\"Generated/sample.json\"," +
                   "\"manifest\":\"Generated/sample.manifest.json\"," +
                   "\"sourceMap\":\"Generated/sample.sourcemap.json\"}]}]}";
        }

        private static byte[] Utf8(string value)
        {
            return new UTF8Encoding(false).GetBytes(value);
        }

        private sealed class FakeCatalogEditor : IConfigAssetCatalogEditor
        {
            private readonly string artifactPath;

            public FakeCatalogEditor(string artifactPath = "Catalog/catalog.json")
            {
                this.artifactPath = artifactPath;
            }

            public System.Collections.Generic.IReadOnlyList<string> InputRelativePaths =>
                new[] { "Catalog/input.txt" };

            public ConfigAssetCatalogPlan Plan(
                string projectRoot,
                string configSetId,
                System.Collections.Generic.IReadOnlyList<ConfigAssetBinding> bindings)
            {
                ConfigAssetBinding binding = bindings.Single();
                byte[] content = CanonicalJsonWriter.WriteUtf8(new ConfigObjectNode(new[]
                {
                    new ConfigProperty("contentId", new ConfigStringNode(binding.ContentId)),
                    new ConfigProperty("assetGuid", new ConfigStringNode(binding.AssetGuid)),
                    new ConfigProperty("expectedType", new ConfigStringNode(binding.ExpectedType))
                }));
                return new ConfigAssetCatalogPlan(
                    new[] { new ConfigArtifact(artifactPath, content) },
                    new[]
                    {
                        new ConfigAssetBindingChange(
                            binding.ContentId,
                            ConfigAssetBindingChangeKind.Added)
                    },
                    Array.Empty<ConfigDiagnostic>());
            }
        }
    }
}
