using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZeroGameStudio.ConfigPipeline.Editor
{
    public sealed class ConfigArtifactGenerationOptions
    {
        public string ToolVersion { get; set; } = "1.0.0";

        public string TargetScope { get; set; }

        public string JsonPath { get; set; }

        public string ManifestPath { get; set; }

        public string SourceMapPath { get; set; }

        public string CodePath { get; set; }

        public string WorkshopSchemaPath { get; set; }

        public string GeneratedNamespace { get; set; }

        public string RootClassName { get; set; }

        public string ContractClassName { get; set; } = "ConfigContract";

        public bool RelaxWorkshopRequired { get; set; }
    }

    public sealed class ConfigArtifactGenerator : IConfigArtifactWriter
    {
        private readonly ConfigSchema schema;
        private readonly ConfigArtifactGenerationOptions options;
        private readonly IReadOnlyList<XlsxSourceMapEntry> sourceMap;

        public ConfigArtifactGenerator(
            ConfigSchema schema,
            ConfigArtifactGenerationOptions options,
            IReadOnlyList<XlsxSourceMapEntry> sourceMap = null)
        {
            this.schema = schema ?? throw new ArgumentNullException(nameof(schema));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.sourceMap = sourceMap ?? Array.Empty<XlsxSourceMapEntry>();
            ValidateOptions(options);
        }

        public IReadOnlyList<ConfigArtifact> Write(
            ConfigDocument document,
            ConfigWriteContext context)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            byte[] json = CanonicalJsonWriter.WriteUtf8(document.Root);
            string sourceHash = ConfigHash.Sha256(json);
            string artifactHash = ConfigHash.Sha256(json);
            var manifest = new ConfigManifest(
                document.ConfigSetId,
                schema.SchemaId,
                schema.SchemaVersion,
                options.ToolVersion,
                schema.SchemaHash,
                sourceHash,
                sourceHash,
                artifactHash,
                options.JsonPath,
                options.TargetScope);

            var artifacts = new List<ConfigArtifact>
            {
                new ConfigArtifact(options.JsonPath, json),
                new ConfigArtifact(
                    options.ManifestPath,
                    CanonicalJsonWriter.WriteUtf8(manifest.ToNode())),
                new ConfigArtifact(options.SourceMapPath, WriteSourceMap(document))
            };
            if (!string.IsNullOrEmpty(options.CodePath))
            {
                artifacts.Add(new ConfigArtifact(
                    options.CodePath,
                    ConfigCodeGenerator.Generate(
                        schema,
                        options.GeneratedNamespace,
                        options.RootClassName,
                        options.ContractClassName)));
            }

            if (!string.IsNullOrEmpty(options.WorkshopSchemaPath))
            {
                artifacts.Add(new ConfigArtifact(
                    options.WorkshopSchemaPath,
                    CanonicalJsonWriter.WriteUtf8(
                        WorkshopSchemaProjector.Project(
                            schema,
                            options.RelaxWorkshopRequired))));
            }

            return artifacts;
        }

        private byte[] WriteSourceMap(ConfigDocument document)
        {
            var entries = new List<ConfigNode>();
            foreach (XlsxSourceMapEntry entry in ConfigSourceMapBuilder
                         .Build(document, schema, sourceMap)
                         .OrderBy(value => value.JsonPath, StringComparer.Ordinal)
                         .ThenBy(value => value.Workbook, StringComparer.Ordinal)
                         .ThenBy(value => value.Sheet, StringComparer.Ordinal)
                         .ThenBy(value => value.Row)
                         .ThenBy(value => value.Column))
            {
                entries.Add(new ConfigObjectNode(new[]
                {
                    new ConfigProperty("jsonPath", new ConfigStringNode(entry.JsonPath)),
                    new ConfigProperty("sourceKind", new ConfigStringNode(entry.SourceKind.ToString())),
                    new ConfigProperty("sourceJsonPath", new ConfigStringNode(entry.SourceJsonPath ?? string.Empty)),
                    new ConfigProperty("schemaPath", new ConfigStringNode(entry.SchemaPath ?? string.Empty)),
                    new ConfigProperty("workbook", new ConfigStringNode(entry.Workbook ?? string.Empty)),
                    new ConfigProperty("sheet", new ConfigStringNode(entry.Sheet ?? string.Empty)),
                    new ConfigProperty("row", new ConfigIntegerNode(entry.Row)),
                    new ConfigProperty("column", new ConfigIntegerNode(entry.Column))
                }));
            }

            return CanonicalJsonWriter.WriteUtf8(
                new ConfigObjectNode(new[]
                {
                    new ConfigProperty("formatVersion", new ConfigIntegerNode(2)),
                    new ConfigProperty("entries", new ConfigArrayNode(entries))
                }));
        }

        private static void ValidateOptions(ConfigArtifactGenerationOptions options)
        {
            if (options.TargetScope != "shared" &&
                options.TargetScope != "client" &&
                options.TargetScope != "server")
            {
                throw new ArgumentException("Target scope is invalid.", nameof(options));
            }

            RequireRelativePath(options.JsonPath, nameof(options.JsonPath));
            RequireRelativePath(options.ManifestPath, nameof(options.ManifestPath));
            RequireRelativePath(options.SourceMapPath, nameof(options.SourceMapPath));
            if (!string.IsNullOrEmpty(options.CodePath))
            {
                RequireRelativePath(options.CodePath, nameof(options.CodePath));
            }

            if (!string.IsNullOrEmpty(options.WorkshopSchemaPath))
            {
                RequireRelativePath(options.WorkshopSchemaPath, nameof(options.WorkshopSchemaPath));
            }
        }

        private static void RequireRelativePath(string path, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                System.IO.Path.IsPathRooted(path) ||
                path.Contains("..") ||
                path.Contains("\\"))
            {
                throw new ArgumentException(
                    "Artifact paths must be normalized project-relative paths.",
                    parameterName);
            }
        }
    }
}
