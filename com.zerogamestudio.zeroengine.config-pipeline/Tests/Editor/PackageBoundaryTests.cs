using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.PackageManager;
using ZeroGameStudio.ConfigPipeline.Editor;

namespace ZeroGameStudio.ConfigPipeline.Tests
{
    [Category("ZGS.ConfigPipeline.CoreContract")]
    public sealed class PackageBoundaryTests
    {
        [Test]
        public void RuntimeAssembly_DoesNotReferenceUnityEngine()
        {
            string[] references = typeof(ConfigDocument).Assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .ToArray();

            Assert.That(references, Does.Not.Contain("UnityEngine"));
            Assert.That(references.Any(name => name.StartsWith("UnityEngine.", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void Package_DoesNotContainConsumerSpecificBranches()
        {
            string root = PackageInfo.FindForAssembly(typeof(ConfigDocument).Assembly).resolvedPath;
            string[] forbidden = { "P" + "OB", "Extr" + "action" };
            IEnumerable<string> files = new[] { "Runtime", "Editor", "Samples~" }
                .SelectMany(directory => Directory.EnumerateFiles(
                    Path.Combine(root, directory),
                    "*",
                    SearchOption.AllDirectories))
                .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                               path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                               path.EndsWith(".md", StringComparison.OrdinalIgnoreCase));

            foreach (string file in files)
            {
                string content = File.ReadAllText(file);
                Assert.That(forbidden.Any(value => content.Contains(value)), Is.False, file);
            }
        }

        [Test]
        public void Sample_WorkbooksNormalizeAndReferencesResolve()
        {
            string root = PackageInfo.FindForAssembly(typeof(ConfigDocument).Assembly).resolvedPath;
            string sample = Path.Combine(root, "Samples~", "MinimalItemDrop", "Config");
            ConfigSchema schema = ConfigSchemaParser.Parse(File.ReadAllBytes(
                Path.Combine(sample, "item-drop.schema.json")));
            var properties = new List<ConfigProperty>();
            foreach (var owner in new[]
                     {
                         new { File = "items.xlsx", Table = "items" },
                         new { File = "drops.xlsx", Table = "dropTables" }
                     })
            {
                using (FileStream stream = File.OpenRead(Path.Combine(sample, owner.File)))
                {
                    XlsxReadResult result = new XlsxConfigSourceReader(
                        schema,
                        null,
                        new[] { owner.Table }).ReadWithSourceMap(
                            stream,
                            new ConfigReadContext(
                                "sample.item-drop",
                                schema.SchemaId,
                                schema.SchemaVersion),
                            owner.File);
                    properties.AddRange(result.Document.Root.Properties);
                }
            }

            var source = new ConfigDocument(
                "sample.item-drop",
                schema.SchemaId,
                schema.SchemaVersion,
                new ConfigObjectNode(properties));
            ConfigNormalizationResult normalized = ConfigSchemaNormalizer.Normalize(source, schema, "client");
            Assert.That(normalized.IsValid, Is.True);
            Assert.That(
                new ConfigReferenceValidator(schema).Validate(
                    normalized.Document,
                    new ConfigValidationContext("client"))
                    .Any(value => value.Severity == ConfigDiagnosticSeverity.Error),
                Is.False);
        }
    }
}
