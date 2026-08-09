using System;
using System.Collections.Generic;
using System.IO;

namespace ZeroGameStudio.ConfigPipeline
{
    public interface IConfigSourceReader
    {
        ConfigDocument Read(Stream source, ConfigReadContext context);
    }

    public interface IConfigArtifactWriter
    {
        IReadOnlyList<ConfigArtifact> Write(ConfigDocument document, ConfigWriteContext context);
    }

    public interface IConfigValidator
    {
        IReadOnlyList<ConfigDiagnostic> Validate(
            ConfigDocument document,
            ConfigValidationContext context);
    }

    public interface IConfigMigration
    {
        string SchemaId { get; }

        int SourceVersion { get; }

        int TargetVersion { get; }

        ConfigDocument Migrate(ConfigDocument source);
    }

    public sealed class ConfigReadContext
    {
        public ConfigReadContext(string configSetId, string schemaId, int schemaVersion)
        {
            ConfigSetId = configSetId ?? throw new ArgumentNullException(nameof(configSetId));
            SchemaId = schemaId ?? throw new ArgumentNullException(nameof(schemaId));
            SchemaVersion = schemaVersion;
        }

        public string ConfigSetId { get; }

        public string SchemaId { get; }

        public int SchemaVersion { get; }
    }

    public sealed class ConfigWriteContext
    {
        public ConfigWriteContext(string targetScope, string outputRoot)
        {
            TargetScope = targetScope ?? throw new ArgumentNullException(nameof(targetScope));
            OutputRoot = outputRoot ?? throw new ArgumentNullException(nameof(outputRoot));
        }

        public string TargetScope { get; }

        public string OutputRoot { get; }
    }

    public sealed class ConfigValidationContext
    {
        public ConfigValidationContext(string targetScope)
        {
            TargetScope = targetScope ?? throw new ArgumentNullException(nameof(targetScope));
        }

        public string TargetScope { get; }
    }

    public sealed class ConfigArtifact
    {
        public ConfigArtifact(string relativePath, byte[] content)
        {
            RelativePath = relativePath ?? throw new ArgumentNullException(nameof(relativePath));
            Content = content ?? throw new ArgumentNullException(nameof(content));
        }

        public string RelativePath { get; }

        public byte[] Content { get; }
    }
}
