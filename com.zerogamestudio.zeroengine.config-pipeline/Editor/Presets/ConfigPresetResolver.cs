using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ZeroGameStudio.ConfigPipeline.Editor
{
    public sealed class ConfigPresetResolutionResult
    {
        internal ConfigPresetResolutionResult(
            ConfigDocument document,
            IEnumerable<XlsxSourceMapEntry> sourceMap,
            IEnumerable<ConfigDiagnostic> diagnostics)
        {
            Document = document;
            SourceMap = new List<XlsxSourceMapEntry>(
                sourceMap ?? Array.Empty<XlsxSourceMapEntry>()).AsReadOnly();
            Diagnostics = new List<ConfigDiagnostic>(
                diagnostics ?? Array.Empty<ConfigDiagnostic>()).AsReadOnly();
        }

        public ConfigDocument Document { get; }

        public IReadOnlyList<XlsxSourceMapEntry> SourceMap { get; }

        public IReadOnlyList<ConfigDiagnostic> Diagnostics { get; }

        public bool IsValid => Document != null &&
                               Diagnostics.All(value => value.Severity != ConfigDiagnosticSeverity.Error);
    }

    public static class ConfigPresetResolver
    {
        public static ConfigPresetResolutionResult Resolve(
            ConfigDocument source,
            ConfigSchema schema,
            IReadOnlyList<XlsxSourceMapEntry> sourceMap)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            var context = new ResolutionContext(source, schema, sourceMap);
            return context.Resolve();
        }

        private sealed class ResolutionContext
        {
            private readonly ConfigDocument source;
            private readonly ConfigSchema schema;
            private readonly List<ConfigDiagnostic> diagnostics = new List<ConfigDiagnostic>();
            private readonly Dictionary<string, XlsxSourceMapEntry> sourceMapByPath =
                new Dictionary<string, XlsxSourceMapEntry>(StringComparer.Ordinal);
            private readonly Dictionary<string, XlsxSourceMapEntry> resolvedMapByPath =
                new Dictionary<string, XlsxSourceMapEntry>(StringComparer.Ordinal);

            public ResolutionContext(
                ConfigDocument source,
                ConfigSchema schema,
                IReadOnlyList<XlsxSourceMapEntry> sourceMap)
            {
                this.source = source;
                this.schema = schema;
                foreach (XlsxSourceMapEntry entry in sourceMap ?? Array.Empty<XlsxSourceMapEntry>())
                {
                    ConfigValueSourceKind kind = IsPresetPath(entry.JsonPath)
                        ? ConfigValueSourceKind.Preset
                        : ConfigValueSourceKind.Instance;
                    var classified = new XlsxSourceMapEntry(
                        entry.JsonPath,
                        entry.Workbook,
                        entry.Sheet,
                        entry.Row,
                        entry.Column,
                        kind,
                        entry.JsonPath,
                        null);
                    sourceMapByPath[entry.JsonPath] = classified;
                    resolvedMapByPath[entry.JsonPath] = classified;
                }

                SeedEmptyContainerSources(source.Root, schema.Root, "$", IsPresetPath);
            }

            public ConfigPresetResolutionResult Resolve()
            {
                var properties = new List<ConfigProperty>();
                foreach (ConfigSchemaProperty property in schema.Root.Properties)
                {
                    if (!source.Root.TryGetValue(property.Name, out ConfigNode value))
                    {
                        continue;
                    }

                    if (property.Schema.PresetSource != null && value is ConfigArrayNode instances)
                    {
                        value = ResolveArray(property.Name, property.Schema, instances);
                    }

                    properties.Add(new ConfigProperty(property.Name, value));
                }

                if (diagnostics.Any(value => value.Severity == ConfigDiagnosticSeverity.Error))
                {
                    return new ConfigPresetResolutionResult(null, resolvedMapByPath.Values, diagnostics);
                }

                return new ConfigPresetResolutionResult(
                    new ConfigDocument(
                        source.ConfigSetId,
                        source.SchemaId,
                        source.SchemaVersion,
                        new ConfigObjectNode(properties)),
                    resolvedMapByPath.Values,
                    diagnostics);
            }

            private ConfigArrayNode ResolveArray(
                string instanceName,
                ConfigSchemaNode instanceSchema,
                ConfigArrayNode instances)
            {
                string presetName = instanceSchema.PresetSource.Substring("#/properties/".Length);
                if (!source.Root.TryGetValue(presetName, out ConfigNode presetValue) ||
                    !(presetValue is ConfigArrayNode presets))
                {
                    Error(
                        "CONFIG_PRESET_SOURCE_MISSING",
                        "$/" + EscapePointer(instanceName),
                        "Preset source '" + presetName + "' is missing.");
                    return instances;
                }

                ConfigSchemaNode presetSchema = FindRootSchema(presetName);
                ConfigSchemaProperty presetKey = presetSchema.Items.Properties.Single(
                    property => property.Schema.PrimaryKey);
                var presetsById = new Dictionary<string, PresetRow>(StringComparer.Ordinal);
                for (int index = 0; index < presets.Items.Count; index++)
                {
                    ConfigObjectNode preset = presets.Items[index] as ConfigObjectNode;
                    if (preset == null ||
                        !preset.TryGetValue(presetKey.Name, out ConfigNode idNode) ||
                        !(idNode is ConfigStringNode id))
                    {
                        continue;
                    }

                    presetsById[id.Value] = new PresetRow(preset, index);
                }

                var resolved = new List<ConfigNode>();
                for (int index = 0; index < instances.Items.Count; index++)
                {
                    ConfigObjectNode instance = instances.Items[index] as ConfigObjectNode;
                    string instancePath = "$/" + EscapePointer(instanceName) + "/" + index;
                    if (instance == null ||
                        !instance.TryGetValue(instanceSchema.PresetReferenceField, out ConfigNode referenceNode) ||
                        !(referenceNode is ConfigStringNode reference) ||
                        string.IsNullOrEmpty(reference.Value))
                    {
                        Error(
                            "CONFIG_PRESET_REFERENCE_MISSING",
                            instancePath + "/" + EscapePointer(instanceSchema.PresetReferenceField),
                            "Preset-derived rows require one typed preset reference.");
                        resolved.Add(instance ?? new ConfigObjectNode(Array.Empty<ConfigProperty>()));
                        continue;
                    }

                    if (!presetsById.TryGetValue(reference.Value, out PresetRow preset))
                    {
                        Error(
                            "CONFIG_PRESET_REFERENCE_DANGLING",
                            instancePath + "/" + EscapePointer(instanceSchema.PresetReferenceField),
                            "Preset '" + reference.Value + "' does not exist in '" + presetName + "'.");
                        resolved.Add(instance);
                        continue;
                    }

                    string presetPath = "$/" + EscapePointer(presetName) + "/" + preset.Index;
                    resolved.Add(ResolveObject(
                        instanceSchema.Items,
                        presetSchema.Items,
                        instance,
                        preset.Value,
                        instancePath,
                        presetPath,
                        instanceSchema.PresetReferenceField));
                }

                return new ConfigArrayNode(resolved);
            }

            private ConfigObjectNode ResolveObject(
                ConfigSchemaNode instanceSchema,
                ConfigSchemaNode presetSchema,
                ConfigObjectNode instance,
                ConfigObjectNode preset,
                string instancePath,
                string presetPath,
                string referenceField)
            {
                var properties = new List<ConfigProperty>();
                foreach (ConfigSchemaProperty instanceProperty in instanceSchema.Properties)
                {
                    bool hasInstance = instance.TryGetValue(instanceProperty.Name, out ConfigNode instanceValue);
                    ConfigSchemaProperty presetProperty = presetSchema.Properties.SingleOrDefault(
                        property => property.Name == instanceProperty.Name);
                    ConfigNode presetValue = null;
                    bool hasPreset = presetProperty != null &&
                                     preset.TryGetValue(instanceProperty.Name, out presetValue);
                    string targetPath = instancePath + "/" + EscapePointer(instanceProperty.Name);
                    string sourcePath = presetPath + "/" + EscapePointer(instanceProperty.Name);

                    if (instanceProperty.Name == referenceField ||
                        instanceProperty.Schema.PrimaryKey ||
                        presetProperty == null)
                    {
                        if (hasInstance)
                        {
                            properties.Add(new ConfigProperty(instanceProperty.Name, instanceValue));
                        }

                        continue;
                    }

                    if (instanceProperty.Schema.Type == ConfigSchemaType.Array)
                    {
                        ConfigNode collection = ResolveCollection(
                            instanceSchema,
                            instanceProperty.Schema,
                            instance,
                            hasInstance ? instanceValue : null,
                            hasPreset ? presetValue : null,
                            targetPath,
                            sourcePath);
                        if (collection != null)
                        {
                            properties.Add(new ConfigProperty(instanceProperty.Name, collection));
                        }

                        continue;
                    }

                    if (instanceProperty.Schema.Type == ConfigSchemaType.Object &&
                        (hasInstance || hasPreset))
                    {
                        var instanceObject = instanceValue as ConfigObjectNode ??
                                             new ConfigObjectNode(Array.Empty<ConfigProperty>());
                        var presetObject = presetValue as ConfigObjectNode ??
                                           new ConfigObjectNode(Array.Empty<ConfigProperty>());
                        properties.Add(new ConfigProperty(
                            instanceProperty.Name,
                            ResolveObject(
                                instanceProperty.Schema,
                                presetProperty.Schema,
                                instanceObject,
                                presetObject,
                                targetPath,
                                sourcePath,
                                null)));
                        continue;
                    }

                    if (hasInstance)
                    {
                        properties.Add(new ConfigProperty(instanceProperty.Name, instanceValue));
                    }
                    else if (hasPreset)
                    {
                        properties.Add(new ConfigProperty(instanceProperty.Name, presetValue));
                        CopyProvenance(sourcePath, targetPath);
                    }
                }

                return new ConfigObjectNode(properties);
            }

            private ConfigNode ResolveCollection(
                ConfigSchemaNode containingObjectSchema,
                ConfigSchemaNode collectionSchema,
                ConfigObjectNode instance,
                ConfigNode instanceValue,
                ConfigNode presetValue,
                string targetPath,
                string sourcePath)
            {
                string modeName = collectionSchema.CollectionOverrideModeField;
                string mode = "Inherit";
                if (instance.TryGetValue(modeName, out ConfigNode modeNode) &&
                    modeNode is ConfigStringNode modeText)
                {
                    mode = modeText.Value;
                }

                if (string.Equals(mode, "Replace", StringComparison.Ordinal))
                {
                    ConfigNode replacement = instanceValue ?? new ConfigArrayNode(Array.Empty<ConfigNode>());
                    if (!resolvedMapByPath.ContainsKey(targetPath))
                    {
                        CopyLocationAs(
                            targetPath,
                            targetPath.Substring(0, targetPath.LastIndexOf('/') + 1) +
                            EscapePointer(modeName),
                            ConfigValueSourceKind.Instance);
                    }

                    return replacement;
                }

                if (!string.Equals(mode, "Inherit", StringComparison.Ordinal))
                {
                    Error(
                        "CONFIG_PRESET_OVERRIDE_MODE_INVALID",
                        targetPath,
                        "Collection override mode must be Inherit or Replace.");
                    return instanceValue;
                }

                if (instanceValue is ConfigArrayNode instanceArray && instanceArray.Items.Count != 0)
                {
                    Error(
                        "CONFIG_PRESET_COLLECTION_REPLACE_REQUIRED",
                        targetPath,
                        "Collection rows require OverrideMode=Replace; append and merge are forbidden.");
                }

                if (presetValue != null)
                {
                    CopyProvenance(sourcePath, targetPath);
                    return presetValue;
                }

                return null;
            }

            private void CopyProvenance(string sourcePath, string targetPath)
            {
                XlsxSourceMapEntry[] entries = sourceMapByPath.Values
                    .Where(entry => entry.JsonPath == sourcePath ||
                                    entry.JsonPath.StartsWith(sourcePath + "/", StringComparison.Ordinal))
                    .ToArray();
                foreach (XlsxSourceMapEntry entry in entries)
                {
                    string suffix = entry.JsonPath.Substring(sourcePath.Length);
                    resolvedMapByPath[targetPath + suffix] = new XlsxSourceMapEntry(
                        targetPath + suffix,
                        entry.Workbook,
                        entry.Sheet,
                        entry.Row,
                        entry.Column,
                        ConfigValueSourceKind.Preset,
                        entry.JsonPath,
                        null);
                }

                if (entries.Length == 0)
                {
                    resolvedMapByPath[targetPath] = new XlsxSourceMapEntry(
                        targetPath,
                        string.Empty,
                        string.Empty,
                        0,
                        0,
                        ConfigValueSourceKind.Preset,
                        sourcePath,
                        null);
                }
            }

            private void CopyLocationAs(
                string targetPath,
                string locationPath,
                ConfigValueSourceKind sourceKind)
            {
                sourceMapByPath.TryGetValue(locationPath, out XlsxSourceMapEntry location);
                resolvedMapByPath[targetPath] = new XlsxSourceMapEntry(
                    targetPath,
                    location?.Workbook ?? string.Empty,
                    location?.Sheet ?? string.Empty,
                    location?.Row ?? 0,
                    location?.Column ?? 0,
                    sourceKind,
                    locationPath,
                    null);
            }

            private void SeedEmptyContainerSources(
                ConfigNode node,
                ConfigSchemaNode nodeSchema,
                string path,
                Func<string, bool> presetClassifier)
            {
                if (node is ConfigArrayNode array)
                {
                    if (array.Items.Count == 0 && path != "$")
                    {
                        ConfigValueSourceKind kind = presetClassifier(path)
                            ? ConfigValueSourceKind.Preset
                            : ConfigValueSourceKind.Instance;
                        if (!resolvedMapByPath.ContainsKey(path))
                        {
                            var entry = new XlsxSourceMapEntry(
                                path,
                                string.Empty,
                                string.Empty,
                                0,
                                0,
                                kind,
                                path,
                                null);
                            sourceMapByPath[path] = entry;
                            resolvedMapByPath[path] = entry;
                        }
                    }

                    for (int index = 0; index < array.Items.Count; index++)
                    {
                        SeedEmptyContainerSources(
                            array.Items[index],
                            nodeSchema.Items,
                            path + "/" + index,
                            presetClassifier);
                    }

                    return;
                }

                if (!(node is ConfigObjectNode configObject))
                {
                    return;
                }

                foreach (ConfigSchemaProperty property in nodeSchema.Properties)
                {
                    if (configObject.TryGetValue(property.Name, out ConfigNode value))
                    {
                        SeedEmptyContainerSources(
                            value,
                            property.Schema,
                            path + "/" + EscapePointer(property.Name),
                            presetClassifier);
                    }
                }
            }

            private bool IsPresetPath(string jsonPath)
            {
                if (!jsonPath.StartsWith("$/", StringComparison.Ordinal))
                {
                    return false;
                }

                int slash = jsonPath.IndexOf('/', 2);
                string rootName = slash < 0 ? jsonPath.Substring(2) : jsonPath.Substring(2, slash - 2);
                ConfigSchemaNode rootSchema = FindRootSchema(UnescapePointer(rootName));
                return rootSchema != null && rootSchema.PresetType != null;
            }

            private ConfigSchemaNode FindRootSchema(string name)
            {
                return schema.Root.Properties
                    .SingleOrDefault(property => property.Name == name)
                    ?.Schema;
            }

            private void Error(string code, string path, string message)
            {
                diagnostics.Add(new ConfigDiagnostic(
                    code,
                    ConfigDiagnosticSeverity.Error,
                    message,
                    source.ConfigSetId,
                    path));
            }
        }

        private sealed class PresetRow
        {
            public PresetRow(ConfigObjectNode value, int index)
            {
                Value = value;
                Index = index;
            }

            public ConfigObjectNode Value { get; }

            public int Index { get; }
        }

        private static string EscapePointer(string value)
        {
            return value.Replace("~", "~0").Replace("/", "~1");
        }

        private static string UnescapePointer(string value)
        {
            return value.Replace("~1", "/").Replace("~0", "~");
        }
    }

    internal static class ConfigSourceMapBuilder
    {
        public static IReadOnlyList<XlsxSourceMapEntry> Build(
            ConfigDocument document,
            ConfigSchema schema,
            IReadOnlyList<XlsxSourceMapEntry> sourceMap)
        {
            var mapped = new Dictionary<string, XlsxSourceMapEntry>(StringComparer.Ordinal);
            foreach (XlsxSourceMapEntry entry in sourceMap ?? Array.Empty<XlsxSourceMapEntry>())
            {
                mapped[entry.JsonPath] = entry;
            }

            var result = new List<XlsxSourceMapEntry>();
            Visit(document.Root, schema.Root, "$", "#", mapped, result);
            return new ReadOnlyCollection<XlsxSourceMapEntry>(result);
        }

        private static void Visit(
            ConfigNode node,
            ConfigSchemaNode schema,
            string jsonPath,
            string schemaPath,
            IReadOnlyDictionary<string, XlsxSourceMapEntry> mapped,
            ICollection<XlsxSourceMapEntry> result)
        {
            if (node is ConfigObjectNode configObject)
            {
                foreach (ConfigSchemaProperty property in schema.Properties)
                {
                    if (configObject.TryGetValue(property.Name, out ConfigNode value))
                    {
                        Visit(
                            value,
                            property.Schema,
                            jsonPath + "/" + EscapePointer(property.Name),
                            schemaPath + "/properties/" + EscapePointer(property.Name),
                            mapped,
                            result);
                    }
                }

                return;
            }

            if (node is ConfigArrayNode array)
            {
                if (array.Items.Count == 0)
                {
                    Add(node, schema, jsonPath, schemaPath, mapped, result);
                    return;
                }

                for (int index = 0; index < array.Items.Count; index++)
                {
                    Visit(
                        array.Items[index],
                        schema.Items,
                        jsonPath + "/" + index,
                        schemaPath + "/items",
                        mapped,
                        result);
                }

                return;
            }

            Add(node, schema, jsonPath, schemaPath, mapped, result);
        }

        private static void Add(
            ConfigNode node,
            ConfigSchemaNode schema,
            string jsonPath,
            string schemaPath,
            IReadOnlyDictionary<string, XlsxSourceMapEntry> mapped,
            ICollection<XlsxSourceMapEntry> result)
        {
            if (mapped.TryGetValue(jsonPath, out XlsxSourceMapEntry entry))
            {
                result.Add(new XlsxSourceMapEntry(
                    jsonPath,
                    entry.Workbook,
                    entry.Sheet,
                    entry.Row,
                    entry.Column,
                    entry.SourceKind,
                    entry.SourceJsonPath,
                    schemaPath));
                return;
            }

            bool isSchemaDefault = schema.DefaultValue != null &&
                                   string.Equals(
                                       CanonicalJsonWriter.WriteText(node),
                                       CanonicalJsonWriter.WriteText(schema.DefaultValue),
                                       StringComparison.Ordinal);
            result.Add(new XlsxSourceMapEntry(
                jsonPath,
                string.Empty,
                string.Empty,
                0,
                0,
                isSchemaDefault ? ConfigValueSourceKind.Schema : ConfigValueSourceKind.Instance,
                isSchemaDefault ? string.Empty : jsonPath,
                isSchemaDefault ? schemaPath + "/default" : schemaPath));
        }

        private static string EscapePointer(string value)
        {
            return value.Replace("~", "~0").Replace("/", "~1");
        }
    }
}
