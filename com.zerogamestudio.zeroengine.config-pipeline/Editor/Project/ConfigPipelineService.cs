using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ZeroGameStudio.ConfigPipeline.Editor
{
    public sealed class ConfigPipelinePreparedPlan
    {
        internal ConfigPipelinePreparedPlan(
            ConfigPipelinePlan plan,
            IReadOnlyList<ConfigArtifact> artifacts,
            IReadOnlyList<ConfigDiagnostic> diagnostics,
            IReadOnlyList<ConfigValueDiff> valueDiffs,
            IReadOnlyList<ConfigEffectiveValue> effectiveValues)
        {
            Plan = plan;
            Artifacts = artifacts;
            Diagnostics = diagnostics;
            ValueDiffs = valueDiffs;
            EffectiveValues = effectiveValues;
        }

        public ConfigPipelinePlan Plan { get; }
        public IReadOnlyList<ConfigArtifact> Artifacts { get; }
        public IReadOnlyList<ConfigDiagnostic> Diagnostics { get; }
        public IReadOnlyList<ConfigValueDiff> ValueDiffs { get; }
        public IReadOnlyList<ConfigEffectiveValue> EffectiveValues { get; }
    }

    public sealed class ConfigSchemaUpgradeCandidateResult
    {
        internal ConfigSchemaUpgradeCandidateResult(
            int sourceVersion,
            int targetVersion,
            string sourceHash,
            int candidateFileCount)
        {
            SourceVersion = sourceVersion;
            TargetVersion = targetVersion;
            SourceHash = sourceHash;
            CandidateFileCount = candidateFileCount;
        }

        public int SourceVersion { get; }
        public int TargetVersion { get; }
        public string SourceHash { get; }
        public int CandidateFileCount { get; }
    }

    public sealed class ConfigWorkbookRefreshCandidateResult
    {
        internal ConfigWorkbookRefreshCandidateResult(string sourceHash, int candidateFileCount)
        {
            SourceHash = sourceHash;
            CandidateFileCount = candidateFileCount;
        }

        public string SourceHash { get; }
        public int CandidateFileCount { get; }
    }

    public sealed partial class ConfigPipelineService
    {
        public ConfigPipelinePreparedPlan Plan(
            string projectRoot,
            string profileRelativePath,
            string configSetId,
            string packageIdentity)
        {
            return PlanInternal(
                projectRoot,
                profileRelativePath,
                configSetId,
                packageIdentity,
                null);
        }

        private ConfigPipelinePreparedPlan PlanInternal(
            string projectRoot,
            string profileRelativePath,
            string configSetId,
            string packageIdentity,
            IReadOnlyDictionary<string, byte[]> workbookOverrides)
        {
            string root = ConfigPathGuard.NormalizeProjectRoot(projectRoot);
            string profilePath = ConfigPathGuard.NormalizeRelativePath(profileRelativePath);
            ConfigProjectProfile profile = ConfigProjectProfileParser.Parse(
                File.ReadAllBytes(ConfigPathGuard.ResolveInside(root, profilePath)));
            ConfigSetProfile set = profile.GetConfigSet(configSetId);
            string schemaAbsolute = ConfigPathGuard.ResolveInside(root, set.SchemaPath);
            ConfigSchema schema = ConfigSchemaParser.Parse(File.ReadAllBytes(schemaAbsolute));
            XlsxReadResult source = ReadWorkbooks(root, set, schema, workbookOverrides);
            ConfigPresetResolutionResult presetResolution = ConfigPresetResolver.Resolve(
                source.Document,
                schema,
                source.SourceMap);
            var diagnostics = new List<ConfigDiagnostic>(presetResolution.Diagnostics);
            ThrowIfErrors(diagnostics);
            var artifacts = new List<ConfigArtifact>();
            var effectiveValues = new List<ConfigEffectiveValue>();
            bool writeCode = true;
            foreach (ConfigTargetProfile target in set.Targets)
            {
                ConfigNormalizationResult normalized = ConfigSchemaNormalizer.Normalize(
                    presetResolution.Document,
                    schema,
                    target.Scope);
                diagnostics.AddRange(normalized.Diagnostics);
                if (!normalized.IsValid)
                {
                    continue;
                }

                var context = new ConfigValidationContext(target.Scope);
                diagnostics.AddRange(new ConfigReferenceValidator(schema).Validate(normalized.Document, context));
                foreach (IConfigValidator validator in ConfigMaintenanceRegistry.GetValidators(configSetId))
                {
                    diagnostics.AddRange(validator.Validate(normalized.Document, context));
                }

                ValidateAssets(
                    schema.Root,
                    normalized.Document.Root,
                    "$",
                    normalized.Document,
                    ConfigMaintenanceRegistry.GetAssetResolver(configSetId),
                    diagnostics);
                if (diagnostics.Any(value => value.Severity == ConfigDiagnosticSeverity.Error))
                {
                    continue;
                }

                effectiveValues.AddRange(ConfigEffectiveValueBuilder.Build(
                    target.Scope,
                    target.JsonPath,
                    normalized.Document,
                    schema,
                    presetResolution.SourceMap));

                artifacts.AddRange(new ConfigArtifactGenerator(
                    schema,
                    new ConfigArtifactGenerationOptions
                    {
                        ToolVersion = packageIdentity,
                        TargetScope = target.Scope,
                        JsonPath = target.JsonPath,
                        ManifestPath = target.ManifestPath,
                        SourceMapPath = target.SourceMapPath,
                        WorkshopSchemaPath = target.WorkshopSchemaPath,
                        CodePath = writeCode ? set.CodePath : null,
                        GeneratedNamespace = set.GeneratedNamespace,
                        RootClassName = set.RootClassName
                    },
                    presetResolution.SourceMap).Write(
                        normalized.Document,
                        new ConfigWriteContext(target.Scope, root)));
                writeCode = false;
            }

            ThrowIfErrors(diagnostics);
            if (workbookOverrides != null)
            {
                foreach (KeyValuePair<string, byte[]> workbook in workbookOverrides)
                {
                    artifacts.Add(new ConfigArtifact(workbook.Key, workbook.Value));
                }
            }

            AddRequiredUnityMetas(root, configSetId, artifacts);
            EnsureUniqueArtifacts(artifacts);
            var inputs = new List<string> { profilePath, set.SchemaPath };
            inputs.AddRange(set.Workbooks.Select(workbook => workbook.Path));
            inputs.AddRange(artifacts
                .Where(artifact => artifact.RelativePath.StartsWith("Assets/", StringComparison.Ordinal) &&
                                   !artifact.RelativePath.EndsWith(".meta", StringComparison.Ordinal))
                .Select(artifact => artifact.RelativePath + ".meta")
                .Where(meta => File.Exists(ConfigPathGuard.ResolveInside(root, meta))));
            ConfigPipelinePlan plan = new ConfigPipelinePlanBuilder().Build(
                root,
                configSetId,
                packageIdentity,
                inputs,
                artifacts);
            var valueDiffs = new List<ConfigValueDiff>();
            foreach (ConfigTargetProfile target in set.Targets)
            {
                ConfigArtifact artifact = artifacts.Single(value => value.RelativePath == target.JsonPath);
                string absolute = ConfigPathGuard.ResolveInside(root, target.JsonPath);
                ConfigNode before = File.Exists(absolute)
                    ? ConfigJsonParser.Parse(File.ReadAllBytes(absolute))
                    : null;
                ConfigNode after = ConfigJsonParser.Parse(artifact.Content);
                valueDiffs.AddRange(ConfigDocumentDiff.Compare(target.JsonPath, before, after));
            }

            return new ConfigPipelinePreparedPlan(
                plan,
                artifacts,
                diagnostics,
                valueDiffs,
                effectiveValues);
        }

        public bool Check(
            string projectRoot,
            string profileRelativePath,
            string configSetId,
            string packageIdentity)
        {
            return Plan(projectRoot, profileRelativePath, configSetId, packageIdentity).Plan.IsCurrent;
        }

        public ConfigApplyResult Apply(
            string projectRoot,
            string profileRelativePath,
            string configSetId,
            string packageIdentity)
        {
            ConfigPipelinePreparedPlan prepared = Plan(
                projectRoot,
                profileRelativePath,
                configSetId,
                packageIdentity);
            return new ConfigTransactionalApplier().Apply(
                projectRoot,
                prepared.Plan,
                packageIdentity,
                prepared.Artifacts,
                () => Check(projectRoot, profileRelativePath, configSetId, packageIdentity));
        }

        public ConfigApplyResult ApplyExpectedPlan(
            string projectRoot,
            string profileRelativePath,
            string configSetId,
            string packageIdentity,
            string expectedPlanId)
        {
            if (string.IsNullOrWhiteSpace(expectedPlanId))
            {
                throw new ArgumentException("Expected Plan ID is required.", nameof(expectedPlanId));
            }

            ConfigPipelinePreparedPlan prepared = Plan(
                projectRoot,
                profileRelativePath,
                configSetId,
                packageIdentity);
            if (!string.Equals(prepared.Plan.PlanId, expectedPlanId, StringComparison.Ordinal))
            {
                throw new ConfigPlanStaleException("CONFIG_PLAN_CHANGED_REPLAN_REQUIRED");
            }

            return new ConfigTransactionalApplier().Apply(
                projectRoot,
                prepared.Plan,
                packageIdentity,
                prepared.Artifacts,
                () => Check(projectRoot, profileRelativePath, configSetId, packageIdentity));
        }

        public void WriteTemplates(
            string projectRoot,
            string profileRelativePath,
            string configSetId,
            string outputDirectory)
        {
            string root = ConfigPathGuard.NormalizeProjectRoot(projectRoot);
            ConfigProjectProfile profile = ConfigProjectProfileParser.Parse(File.ReadAllBytes(
                ConfigPathGuard.ResolveInside(root, profileRelativePath)));
            ConfigSetProfile set = profile.GetConfigSet(configSetId);
            ConfigSchema schema = ConfigSchemaParser.Parse(File.ReadAllBytes(
                ConfigPathGuard.ResolveInside(root, set.SchemaPath)));
            Directory.CreateDirectory(outputDirectory);
            foreach (ConfigWorkbookProfile workbook in set.Workbooks)
            {
                string destination = Path.Combine(outputDirectory, Path.GetFileName(workbook.Path));
                using (FileStream stream = File.Create(destination))
                {
                    new XlsxConfigWorkbookWriter().WriteTemplate(
                        stream,
                        schema,
                        configSetId,
                        null,
                        null,
                        workbook.Tables);
                }
            }
        }

        public ConfigWorkbookRefreshCandidateResult ExportWorkbookRefreshCandidate(
            string projectRoot,
            string profileRelativePath,
            string configSetId,
            string outputDirectory)
        {
            string root = ConfigPathGuard.NormalizeProjectRoot(projectRoot);
            ConfigProjectProfile profile = ConfigProjectProfileParser.Parse(File.ReadAllBytes(
                ConfigPathGuard.ResolveInside(root, profileRelativePath)));
            ConfigSetProfile set = profile.GetConfigSet(configSetId);
            ConfigSchema schema = ConfigSchemaParser.Parse(File.ReadAllBytes(
                ConfigPathGuard.ResolveInside(root, set.SchemaPath)));
            XlsxReadResult source = ReadWorkbooks(root, set, schema);
            byte[] sourceBytes = CanonicalJsonWriter.WriteUtf8(source.Document.Root);
            string sourceHash = ConfigHash.Sha256(sourceBytes);
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new ArgumentException("Workbook refresh candidate output directory is required.",
                    nameof(outputDirectory));
            }

            string output = Path.GetFullPath(outputDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (Directory.Exists(output) || File.Exists(output))
            {
                throw new InvalidOperationException(
                    "Workbook refresh candidate output directory must not already exist.");
            }

            string parent = Path.GetDirectoryName(output);
            string outputName = Path.GetFileName(output);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(outputName))
            {
                throw new InvalidOperationException(
                    "Workbook refresh candidate output directory requires a parent directory.");
            }

            Directory.CreateDirectory(parent);
            string staging = Path.Combine(
                parent,
                "." + outputName + ".staging." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            var candidates = new Dictionary<ConfigWorkbookProfile, string>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (ConfigWorkbookProfile workbook in set.Workbooks)
                {
                    string name = Path.GetFileNameWithoutExtension(workbook.Path) + ".candidate.xlsx";
                    if (!names.Add(name))
                    {
                        throw new InvalidOperationException("Candidate workbook names must be unique.");
                    }

                    string candidatePath = Path.Combine(staging, name);
                    candidates.Add(workbook, candidatePath);
                    using (FileStream stream = new FileStream(
                               candidatePath,
                               FileMode.CreateNew,
                               FileAccess.ReadWrite,
                               FileShare.None))
                    {
                        new XlsxConfigWorkbookWriter().WriteTemplate(
                            stream,
                            schema,
                            configSetId,
                            source.Document,
                            sourceHash,
                            workbook.Tables);
                    }
                }

                XlsxReadResult roundTrip = ReadWorkbooks(
                    set,
                    schema,
                    workbook => candidates[workbook],
                    workbook => Path.Combine(output, Path.GetFileName(candidates[workbook])));
                byte[] roundTripBytes = CanonicalJsonWriter.WriteUtf8(roundTrip.Document.Root);
                if (!sourceBytes.SequenceEqual(roundTripBytes))
                {
                    throw new InvalidDataException("CONFIG_WORKBOOK_REFRESH_DATA_MISMATCH");
                }

                Directory.Move(staging, output);

                return new ConfigWorkbookRefreshCandidateResult(sourceHash, candidates.Count);
            }
            catch
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, true);
                }

                throw;
            }
        }

        public ConfigImportConflictResult ExportJsonCandidate(
            string projectRoot,
            string profileRelativePath,
            string configSetId,
            string targetScope,
            string outputDirectory)
        {
            string root = ConfigPathGuard.NormalizeProjectRoot(projectRoot);
            ConfigProjectProfile profile = ConfigProjectProfileParser.Parse(File.ReadAllBytes(
                ConfigPathGuard.ResolveInside(root, profileRelativePath)));
            ConfigSetProfile set = profile.GetConfigSet(configSetId);
            ConfigTargetProfile target = set.Targets.Single(value => value.Scope == targetScope);
            ConfigSchema schema = ConfigSchemaParser.Parse(File.ReadAllBytes(
                ConfigPathGuard.ResolveInside(root, set.SchemaPath)));
            XlsxReadResult workbookSource = ReadWorkbooks(root, set, schema);
            ConfigPresetResolutionResult presetResolution = ConfigPresetResolver.Resolve(
                workbookSource.Document,
                schema,
                workbookSource.SourceMap);
            if (!presetResolution.IsValid)
            {
                throw new ConfigPipelineValidationException(presetResolution.Diagnostics);
            }

            ConfigNormalizationResult normalizedWorkbook = ConfigSchemaNormalizer.Normalize(
                presetResolution.Document,
                schema,
                targetScope);
            if (!normalizedWorkbook.IsValid)
            {
                throw new ConfigPipelineValidationException(normalizedWorkbook.Diagnostics);
            }

            byte[] json = File.ReadAllBytes(ConfigPathGuard.ResolveInside(root, target.JsonPath));
            string manifestAbsolute = ConfigPathGuard.ResolveInside(root, target.ManifestPath);
            ConfigDocument jsonDocument;
            string baseSourceHash = null;
            string jsonSourceHash;
            if (File.Exists(manifestAbsolute))
            {
                byte[] manifestBytes = File.ReadAllBytes(manifestAbsolute);
                ConfigManifest manifest = ConfigManifest.Parse(manifestBytes);
                jsonDocument = new ConfigArtifactReader().Read(
                    json,
                    manifestBytes,
                    new ConfigArtifactContract(
                        configSetId,
                        schema.SchemaId,
                        schema.SchemaVersion,
                        schema.SchemaHash));
                baseSourceHash = manifest.BaseSourceHash;
                jsonSourceHash = manifest.SourceHash;
            }
            else
            {
                ConfigNode parsed = ConfigJsonParser.Parse(json);
                if (!(parsed is ConfigObjectNode rootNode))
                {
                    throw new InvalidDataException("Imported JSON root must be an object.");
                }

                jsonDocument = new ConfigDocument(
                    configSetId,
                    schema.SchemaId,
                    schema.SchemaVersion,
                    rootNode);
                jsonSourceHash = ConfigHash.Sha256(CanonicalJsonWriter.WriteUtf8(rootNode));
            }

            ConfigNormalizationResult normalizedJson = ConfigSchemaNormalizer.Normalize(
                jsonDocument,
                schema,
                targetScope);
            if (!normalizedJson.IsValid)
            {
                throw new ConfigPipelineValidationException(normalizedJson.Diagnostics);
            }

            string workbookCurrentHash = ConfigHash.Sha256(
                CanonicalJsonWriter.WriteUtf8(normalizedWorkbook.Document.Root));
            ConfigImportConflictResult conflict = ConfigImportConflictResolver.Resolve(
                baseSourceHash,
                jsonSourceHash,
                workbookCurrentHash);
            if (!conflict.CanCreateCandidate)
            {
                throw new InvalidOperationException(conflict.DiagnosticCode);
            }

            Directory.CreateDirectory(outputDirectory);
            ConfigDocument candidateDocument = new ConfigDocument(
                configSetId,
                schema.SchemaId,
                schema.SchemaVersion,
                (ConfigObjectNode)MergeProjection(
                    schema.Root,
                    workbookSource.Document.Root,
                    normalizedJson.Document.Root,
                    targetScope));
            foreach (ConfigWorkbookProfile workbook in set.Workbooks)
            {
                string name = Path.GetFileNameWithoutExtension(workbook.Path) + ".candidate.xlsx";
                using (FileStream stream = File.Create(Path.Combine(outputDirectory, name)))
                {
                    new XlsxConfigWorkbookWriter().WriteTemplate(
                        stream,
                        schema,
                        configSetId,
                        candidateDocument,
                        jsonSourceHash,
                        workbook.Tables);
                }
            }

            return conflict;
        }

        public ConfigSchemaUpgradeCandidateResult ExportSchemaUpgradeCandidate(
            string projectRoot,
            string profileRelativePath,
            string nextProfileRelativePath,
            string configSetId,
            string packageIdentity,
            string outputDirectory)
        {
            string root = ConfigPathGuard.NormalizeProjectRoot(projectRoot);
            string currentProfilePath = ConfigPathGuard.NormalizeRelativePath(profileRelativePath);
            string nextProfilePath = ConfigPathGuard.NormalizeRelativePath(nextProfileRelativePath);
            ConfigProjectProfile currentProfile = ConfigProjectProfileParser.Parse(File.ReadAllBytes(
                ConfigPathGuard.ResolveInside(root, currentProfilePath)));
            ConfigProjectProfile nextProfile = ConfigProjectProfileParser.Parse(File.ReadAllBytes(
                ConfigPathGuard.ResolveInside(root, nextProfilePath)));
            ConfigSetProfile currentSet = currentProfile.GetConfigSet(configSetId);
            ConfigSetProfile nextSet = nextProfile.GetConfigSet(configSetId);
            ConfigSchema currentSchema = ConfigSchemaParser.Parse(File.ReadAllBytes(
                ConfigPathGuard.ResolveInside(root, currentSet.SchemaPath)));
            ConfigSchema nextSchema = ConfigSchemaParser.Parse(File.ReadAllBytes(
                ConfigPathGuard.ResolveInside(root, nextSet.SchemaPath)));
            if (!string.Equals(currentSchema.SchemaId, nextSchema.SchemaId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Schema upgrade cannot change the schema ID.");
            }

            if (nextSchema.SchemaVersion <= currentSchema.SchemaVersion)
            {
                throw new InvalidOperationException("Next Schema version must be greater than the current version.");
            }

            if (!Check(root, currentProfilePath, configSetId, packageIdentity))
            {
                throw new InvalidOperationException("CONFIG_UPGRADE_BASE_STALE");
            }

            ConfigDocument source = ReadWorkbooks(root, currentSet, currentSchema).Document;
            IReadOnlyList<IConfigMigration> migrations = ConfigMaintenanceRegistry.GetMigrations(configSetId);
            ConfigDocument upgraded = migrations.Count == 0
                ? new ConfigDocument(
                    source.ConfigSetId,
                    source.SchemaId,
                    nextSchema.SchemaVersion,
                    source.Root)
                : new ConfigMigrationRegistry(migrations).Migrate(source, nextSchema.SchemaVersion);
            var diagnostics = new List<ConfigDiagnostic>();
            ConfigPresetResolutionResult presetResolution = ConfigPresetResolver.Resolve(
                upgraded,
                nextSchema,
                Array.Empty<XlsxSourceMapEntry>());
            diagnostics.AddRange(presetResolution.Diagnostics);
            ThrowIfErrors(diagnostics);
            foreach (ConfigTargetProfile target in nextSet.Targets)
            {
                ConfigNormalizationResult normalized = ConfigSchemaNormalizer.Normalize(
                    presetResolution.Document,
                    nextSchema,
                    target.Scope);
                diagnostics.AddRange(normalized.Diagnostics);
                if (!normalized.IsValid)
                {
                    continue;
                }

                var context = new ConfigValidationContext(target.Scope);
                diagnostics.AddRange(new ConfigReferenceValidator(nextSchema).Validate(
                    normalized.Document,
                    context));
                foreach (IConfigValidator validator in ConfigMaintenanceRegistry.GetValidators(configSetId))
                {
                    diagnostics.AddRange(validator.Validate(normalized.Document, context));
                }

                ValidateAssets(
                    nextSchema.Root,
                    normalized.Document.Root,
                    "$",
                    normalized.Document,
                    ConfigMaintenanceRegistry.GetAssetResolver(configSetId),
                    diagnostics);
            }

            ThrowIfErrors(diagnostics);
            string sourceHash = ConfigHash.Sha256(CanonicalJsonWriter.WriteUtf8(source.Root));
            Directory.CreateDirectory(outputDirectory);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ConfigWorkbookProfile workbook in nextSet.Workbooks)
            {
                string name = Path.GetFileNameWithoutExtension(workbook.Path) + ".candidate.xlsx";
                if (!names.Add(name))
                {
                    throw new InvalidOperationException("Candidate workbook names must be unique.");
                }

                using (FileStream stream = File.Create(Path.Combine(outputDirectory, name)))
                {
                    new XlsxConfigWorkbookWriter().WriteTemplate(
                        stream,
                        nextSchema,
                        configSetId,
                        upgraded,
                        sourceHash,
                        workbook.Tables);
                }
            }

            return new ConfigSchemaUpgradeCandidateResult(
                currentSchema.SchemaVersion,
                nextSchema.SchemaVersion,
                sourceHash,
                names.Count);
        }

        private static ConfigNode MergeProjection(
            ConfigSchemaNode schema,
            ConfigNode current,
            ConfigNode projected,
            string targetScope)
        {
            if (!AppliesToTarget(schema.Scope, targetScope))
            {
                return current;
            }

            if (schema.Type == ConfigSchemaType.Object &&
                current is ConfigObjectNode currentObject &&
                projected is ConfigObjectNode projectedObject)
            {
                var properties = new List<ConfigProperty>();
                foreach (ConfigSchemaProperty property in schema.Properties)
                {
                    bool hasCurrent = currentObject.TryGetValue(property.Name, out ConfigNode currentValue);
                    bool hasProjected = projectedObject.TryGetValue(property.Name, out ConfigNode projectedValue);
                    if (!AppliesToTarget(property.Schema.Scope, targetScope))
                    {
                        if (hasCurrent)
                        {
                            properties.Add(new ConfigProperty(property.Name, currentValue));
                        }
                    }
                    else if (hasProjected)
                    {
                        properties.Add(new ConfigProperty(
                            property.Name,
                            hasCurrent
                                ? MergeProjection(
                                    property.Schema,
                                    currentValue,
                                    projectedValue,
                                    targetScope)
                                : projectedValue));
                    }
                }

                return new ConfigObjectNode(properties);
            }

            if (schema.Type == ConfigSchemaType.Array &&
                current is ConfigArrayNode currentArray &&
                projected is ConfigArrayNode projectedArray &&
                schema.Items?.Type == ConfigSchemaType.Object)
            {
                ConfigSchemaProperty primary = schema.Items.Properties.SingleOrDefault(
                    property => property.Schema.PrimaryKey);
                if (primary != null)
                {
                    Dictionary<string, ConfigNode> currentById = currentArray.Items
                        .OfType<ConfigObjectNode>()
                        .Where(value => value.TryGetValue(primary.Name, out ConfigNode id) &&
                                        id is ConfigStringNode)
                        .ToDictionary(
                            value => ((ConfigStringNode)value.Properties.Single(
                                property => property.Name == primary.Name).Value).Value,
                            value => (ConfigNode)value,
                            StringComparer.Ordinal);
                    var merged = new List<ConfigNode>();
                    foreach (ConfigNode projectedItem in projectedArray.Items)
                    {
                        var projectedRow = (ConfigObjectNode)projectedItem;
                        string id = ((ConfigStringNode)projectedRow.Properties.Single(
                            property => property.Name == primary.Name).Value).Value;
                        merged.Add(currentById.TryGetValue(id, out ConfigNode currentItem)
                            ? MergeProjection(schema.Items, currentItem, projectedItem, targetScope)
                            : projectedItem);
                    }

                    return new ConfigArrayNode(merged);
                }
            }

            return projected;
        }

        private static bool AppliesToTarget(ConfigFieldScope scope, string targetScope)
        {
            switch (scope)
            {
                case ConfigFieldScope.Shared:
                    return true;
                case ConfigFieldScope.Client:
                    return targetScope == "client";
                case ConfigFieldScope.Server:
                    return targetScope == "server";
                default:
                    throw new ArgumentOutOfRangeException(nameof(scope));
            }
        }

        internal static XlsxReadResult ReadWorkbooks(
            string root,
            ConfigSetProfile set,
            ConfigSchema schema)
        {
            return ReadWorkbooks(root, set, schema, null);
        }

        private static XlsxReadResult ReadWorkbooks(
            string root,
            ConfigSetProfile set,
            ConfigSchema schema,
            IReadOnlyDictionary<string, byte[]> workbookOverrides)
        {
            return ReadWorkbooks(
                set,
                schema,
                workbook =>
                {
                    string path = ConfigPathGuard.NormalizeRelativePath(workbook.Path);
                    if (workbookOverrides != null &&
                        workbookOverrides.TryGetValue(path, out byte[] content))
                    {
                        return new MemoryStream(content, false);
                    }

                    return File.OpenRead(ConfigPathGuard.ResolveInside(root, path));
                },
                workbook => workbook.Path);
        }

        private static XlsxReadResult ReadWorkbooks(
            ConfigSetProfile set,
            ConfigSchema schema,
            Func<ConfigWorkbookProfile, string> pathResolver,
            Func<ConfigWorkbookProfile, string> workbookNameResolver)
        {
            return ReadWorkbooks(
                set,
                schema,
                workbook => File.OpenRead(pathResolver(workbook)),
                workbookNameResolver);
        }

        private static XlsxReadResult ReadWorkbooks(
            ConfigSetProfile set,
            ConfigSchema schema,
            Func<ConfigWorkbookProfile, Stream> streamResolver,
            Func<ConfigWorkbookProfile, string> workbookNameResolver)
        {
            var properties = new Dictionary<string, ConfigNode>(StringComparer.Ordinal);
            var sourceMap = new List<XlsxSourceMapEntry>();
            foreach (ConfigWorkbookProfile workbook in set.Workbooks)
            {
                using (Stream stream = streamResolver(workbook))
                {
                    XlsxReadResult read = new XlsxConfigSourceReader(
                        schema,
                        null,
                        workbook.Tables).ReadWithSourceMap(
                            stream,
                            new ConfigReadContext(set.ConfigSetId, schema.SchemaId, schema.SchemaVersion),
                            workbookNameResolver(workbook));
                    foreach (ConfigProperty property in read.Document.Root.Properties)
                    {
                        if (properties.ContainsKey(property.Name))
                        {
                            throw new InvalidDataException("Table has multiple workbook owners: " + property.Name);
                        }

                        properties.Add(property.Name, property.Value);
                    }

                    sourceMap.AddRange(read.SourceMap);
                }
            }

            var ordered = new List<ConfigProperty>();
            foreach (ConfigSchemaProperty property in schema.Root.Properties)
            {
                if (properties.TryGetValue(property.Name, out ConfigNode value))
                {
                    ordered.Add(new ConfigProperty(property.Name, value));
                }
            }

            if (ordered.Count != properties.Count)
            {
                throw new InvalidDataException("Workbook produced a table not declared by the schema.");
            }

            return new XlsxReadResult(
                new ConfigDocument(set.ConfigSetId, schema.SchemaId, schema.SchemaVersion, new ConfigObjectNode(ordered)),
                string.Empty,
                sourceMap);
        }

        private static void ValidateAssets(
            ConfigSchemaNode schema,
            ConfigNode value,
            string path,
            ConfigDocument document,
            IConfigAssetResolver resolver,
            List<ConfigDiagnostic> diagnostics)
        {
            if (!string.IsNullOrEmpty(schema.AssetType) && value is ConfigStringNode contentId)
            {
                if (resolver == null)
                {
                    diagnostics.Add(new ConfigDiagnostic(
                        "CONFIG_ASSET_RESOLVER_REQUIRED",
                        ConfigDiagnosticSeverity.Error,
                        "An asset resolver is required for contentId '" + contentId.Value + "'.",
                        document.ConfigSetId,
                        path));
                }
                else
                {
                    ConfigDiagnostic result = resolver.Validate(
                        document.ConfigSetId,
                        path,
                        contentId.Value,
                        schema.AssetType);
                    if (result != null)
                    {
                        diagnostics.Add(result);
                    }
                }
            }

            if (schema.Type == ConfigSchemaType.Object && value is ConfigObjectNode objectValue)
            {
                foreach (ConfigSchemaProperty property in schema.Properties)
                {
                    if (objectValue.TryGetValue(property.Name, out ConfigNode child))
                    {
                        ValidateAssets(property.Schema, child, path + "." + property.Name, document, resolver, diagnostics);
                    }
                }
            }
            else if (schema.Type == ConfigSchemaType.Array && value is ConfigArrayNode array)
            {
                for (int index = 0; index < array.Items.Count; index++)
                {
                    ValidateAssets(schema.Items, array.Items[index], path + "[" + index + "]", document, resolver, diagnostics);
                }
            }
        }

        private static void ThrowIfErrors(IEnumerable<ConfigDiagnostic> diagnostics)
        {
            List<ConfigDiagnostic> errors = diagnostics
                .Where(value => value.Severity == ConfigDiagnosticSeverity.Error)
                .ToList();
            if (errors.Count != 0)
            {
                throw new ConfigPipelineValidationException(errors);
            }
        }

        private static void EnsureUniqueArtifacts(IEnumerable<ConfigArtifact> artifacts)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConfigArtifact artifact in artifacts)
            {
                if (!paths.Add(artifact.RelativePath))
                {
                    throw new InvalidDataException("Multiple targets produce artifact: " + artifact.RelativePath);
                }
            }
        }

        internal static void AddRequiredUnityMetas(
            string projectRoot,
            string configSetId,
            List<ConfigArtifact> artifacts)
        {
            var directoryPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConfigArtifact artifact in artifacts)
            {
                string path = ConfigPathGuard.NormalizeRelativePath(artifact.RelativePath);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) ||
                    path.EndsWith(".meta", StringComparison.Ordinal))
                {
                    continue;
                }

                string directory = Path.GetDirectoryName(path)?.Replace('\\', '/');
                while (!string.IsNullOrEmpty(directory) &&
                       !string.Equals(directory, "Assets", StringComparison.Ordinal))
                {
                    directoryPaths.Add(directory);
                    directory = Path.GetDirectoryName(directory)?.Replace('\\', '/');
                }
            }

            foreach (string directoryPath in directoryPaths.OrderBy(value => value, StringComparer.Ordinal))
            {
                AddUnityMeta(projectRoot, configSetId, artifacts, directoryPath, true);
            }

            foreach (ConfigArtifact artifact in artifacts.ToList())
            {
                string path = ConfigPathGuard.NormalizeRelativePath(artifact.RelativePath);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) ||
                    path.EndsWith(".meta", StringComparison.Ordinal))
                {
                    continue;
                }

                AddUnityMeta(projectRoot, configSetId, artifacts, path, false);
            }
        }

        private static void AddUnityMeta(
            string projectRoot,
            string configSetId,
            List<ConfigArtifact> artifacts,
            string assetPath,
            bool isDirectory)
        {
            string metaPath = assetPath + ".meta";
            if (File.Exists(ConfigPathGuard.ResolveInside(projectRoot, metaPath)) ||
                artifacts.Any(value => value.RelativePath == metaPath))
            {
                return;
            }

            string identity = isDirectory
                ? "unity-directory:" + assetPath
                : configSetId + ":" + assetPath;
            string guid = ConfigHash.Sha256(
                System.Text.Encoding.UTF8.GetBytes(identity)).Substring(0, 32);
            string importer = !isDirectory && assetPath.EndsWith(".cs", StringComparison.Ordinal)
                ? "MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n"
                : "DefaultImporter:\n  externalObjects: {}\n";
            string folderAsset = isDirectory ? "folderAsset: yes\n" : string.Empty;
            string content = "fileFormatVersion: 2\nguid: " + guid + "\n" + folderAsset + importer +
                             "  userData: \n  assetBundleName: \n  assetBundleVariant:\n";
            artifacts.Add(new ConfigArtifact(
                metaPath,
                new System.Text.UTF8Encoding(false).GetBytes(content)));
        }
    }

    public sealed class ConfigPipelineValidationException : Exception
    {
        public ConfigPipelineValidationException(IReadOnlyList<ConfigDiagnostic> diagnostics)
            : base(string.Join(Environment.NewLine, diagnostics.Select(value => value.Code + ": " + value.Message)))
        {
            Diagnostics = diagnostics;
        }

        public IReadOnlyList<ConfigDiagnostic> Diagnostics { get; }
    }
}
