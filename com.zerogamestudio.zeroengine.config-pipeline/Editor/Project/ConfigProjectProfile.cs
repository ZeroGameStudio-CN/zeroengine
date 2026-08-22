using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace ZeroGameStudio.ConfigPipeline.Editor
{
    public sealed class ConfigProjectProfile
    {
        internal ConfigProjectProfile(IEnumerable<ConfigSetProfile> configSets)
        {
            ConfigSets = new List<ConfigSetProfile>(configSets).AsReadOnly();
        }

        public IReadOnlyList<ConfigSetProfile> ConfigSets { get; }

        public ConfigSetProfile GetConfigSet(string configSetId)
        {
            return ConfigSets.Single(set => string.Equals(set.ConfigSetId, configSetId, StringComparison.Ordinal));
        }
    }

    public sealed class ConfigSetProfile
    {
        internal ConfigSetProfile(
            string configSetId,
            string authoringSource,
            string schemaPath,
            IEnumerable<ConfigWorkbookProfile> workbooks,
            string generatedNamespace,
            string rootClassName,
            string codePath,
            IEnumerable<ConfigTargetProfile> targets)
        {
            ConfigSetId = configSetId;
            AuthoringSource = authoringSource;
            SchemaPath = schemaPath;
            Workbooks = new List<ConfigWorkbookProfile>(workbooks).AsReadOnly();
            GeneratedNamespace = generatedNamespace;
            RootClassName = rootClassName;
            CodePath = codePath;
            Targets = new List<ConfigTargetProfile>(targets).AsReadOnly();
        }

        public string ConfigSetId { get; }
        public string AuthoringSource { get; }
        public string SchemaPath { get; }
        public IReadOnlyList<ConfigWorkbookProfile> Workbooks { get; }
        public string GeneratedNamespace { get; }
        public string RootClassName { get; }
        public string CodePath { get; }
        public IReadOnlyList<ConfigTargetProfile> Targets { get; }
    }

    public sealed class ConfigWorkbookProfile
    {
        internal ConfigWorkbookProfile(
            string path,
            IEnumerable<string> tables,
            IEnumerable<ConfigAuthoringSheetProfile> authoringSheets)
        {
            Path = path;
            Tables = new List<string>(tables).AsReadOnly();
            AuthoringSheets = new List<ConfigAuthoringSheetProfile>(
                authoringSheets ?? Array.Empty<ConfigAuthoringSheetProfile>()).AsReadOnly();
        }

        public string Path { get; }
        public IReadOnlyList<string> Tables { get; }
        public IReadOnlyList<ConfigAuthoringSheetProfile> AuthoringSheets { get; }
    }

    public sealed class ConfigAuthoringSheetProfile
    {
        internal ConfigAuthoringSheetProfile(string name, IEnumerable<string> tables)
        {
            Name = name;
            Tables = new List<string>(tables).AsReadOnly();
        }

        public string Name { get; }
        public IReadOnlyList<string> Tables { get; }
    }

    public sealed class ConfigTargetProfile
    {
        internal ConfigTargetProfile(
            string scope,
            string jsonPath,
            string manifestPath,
            string sourceMapPath,
            string workshopSchemaPath)
        {
            Scope = scope;
            JsonPath = jsonPath;
            ManifestPath = manifestPath;
            SourceMapPath = sourceMapPath;
            WorkshopSchemaPath = workshopSchemaPath;
        }

        public string Scope { get; }
        public string JsonPath { get; }
        public string ManifestPath { get; }
        public string SourceMapPath { get; }
        public string WorkshopSchemaPath { get; }
    }

    public static class ConfigProjectProfileParser
    {
        private static readonly HashSet<string> RootKeys = Keys("formatVersion", "configSets");
        private static readonly HashSet<string> SetKeys = Keys(
            "configSetId", "authoringSource", "schema", "workbooks",
            "generatedNamespace", "rootClassName", "codePath", "targets");
        private static readonly HashSet<string> WorkbookKeys = Keys("path", "tables", "authoringSheets");
        private static readonly HashSet<string> AuthoringSheetKeys = Keys("name", "tables");
        private static readonly HashSet<string> TargetKeys = Keys(
            "scope", "json", "manifest", "sourceMap", "workshopSchema");

        public static ConfigProjectProfile Parse(byte[] utf8)
        {
            ConfigObjectNode root = RequireObject(ConfigJsonParser.Parse(utf8), "$", RootKeys);
            int formatVersion = checked((int)RequireInteger(root, "formatVersion", "$"));
            if (formatVersion != 1)
            {
                throw new InvalidDataException("Unsupported config project formatVersion: " + formatVersion);
            }

            ConfigArrayNode setsNode = RequireArray(root, "configSets", "$");
            var sets = new List<ConfigSetProfile>();
            var setIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConfigNode node in setsNode.Items)
            {
                ConfigObjectNode set = RequireObject(node, "$.configSets[]", SetKeys);
                string id = RequireString(set, "configSetId", "$.configSets[]");
                if (!setIds.Add(id))
                {
                    throw new InvalidDataException("Duplicate configSetId: " + id);
                }

                string authoringSource = RequireString(set, "authoringSource", id);
                if (authoringSource != "excel")
                {
                    throw new InvalidDataException("Only authoringSource 'excel' is supported.");
                }

                var workbooks = new List<ConfigWorkbookProfile>();
                var tableOwners = new HashSet<string>(StringComparer.Ordinal);
                var workbookPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (ConfigNode workbookNode in RequireArray(set, "workbooks", id).Items)
                {
                    ConfigObjectNode workbook = RequireObject(workbookNode, id + ".workbooks[]", WorkbookKeys);
                    string path = NormalizePath(RequireString(workbook, "path", id));
                    if (!workbookPaths.Add(path))
                    {
                        throw new InvalidDataException("Duplicate workbook path: " + path);
                    }

                    var tables = ReadStrings(RequireArray(workbook, "tables", id), id + ".tables");
                    if (tables.Count == 0)
                    {
                        throw new InvalidDataException("A workbook must own at least one table.");
                    }

                    foreach (string table in tables)
                    {
                        if (!tableOwners.Add(table))
                        {
                            throw new InvalidDataException("A table has multiple workbook owners: " + table);
                        }
                    }

                    IReadOnlyList<ConfigAuthoringSheetProfile> authoringSheets =
                        ReadAuthoringSheets(workbook, tables, id + ".workbooks[]");
                    workbooks.Add(new ConfigWorkbookProfile(path, tables, authoringSheets));
                }

                var targets = new List<ConfigTargetProfile>();
                foreach (ConfigNode targetNode in RequireArray(set, "targets", id).Items)
                {
                    ConfigObjectNode target = RequireObject(targetNode, id + ".targets[]", TargetKeys);
                    string scope = RequireString(target, "scope", id);
                    if (scope != "shared" && scope != "client" && scope != "server")
                    {
                        throw new InvalidDataException("Invalid target scope: " + scope);
                    }

                    targets.Add(new ConfigTargetProfile(
                        scope,
                        NormalizePath(RequireString(target, "json", id)),
                        NormalizePath(RequireString(target, "manifest", id)),
                        NormalizePath(RequireString(target, "sourceMap", id)),
                        OptionalPath(target, "workshopSchema")));
                }

                if (workbooks.Count == 0 || targets.Count == 0)
                {
                    throw new InvalidDataException("Each config set requires workbooks and targets.");
                }

                sets.Add(new ConfigSetProfile(
                    id,
                    authoringSource,
                    NormalizePath(RequireString(set, "schema", id)),
                    workbooks,
                    RequireString(set, "generatedNamespace", id),
                    RequireString(set, "rootClassName", id),
                    NormalizePath(RequireString(set, "codePath", id)),
                    targets));
            }

            return new ConfigProjectProfile(sets);
        }

        private static ConfigObjectNode RequireObject(ConfigNode value, string path, ISet<string> allowed)
        {
            if (!(value is ConfigObjectNode result))
            {
                throw new InvalidDataException(path + " must be an object.");
            }

            foreach (ConfigProperty property in result.Properties)
            {
                if (!allowed.Contains(property.Name))
                {
                    throw new InvalidDataException(path + " contains unknown property '" + property.Name + "'.");
                }
            }

            return result;
        }

        private static ConfigArrayNode RequireArray(ConfigObjectNode value, string property, string path)
        {
            if (!value.TryGetValue(property, out ConfigNode node) || !(node is ConfigArrayNode array))
            {
                throw new InvalidDataException(path + "." + property + " must be an array.");
            }

            return array;
        }

        private static string RequireString(ConfigObjectNode value, string property, string path)
        {
            if (!value.TryGetValue(property, out ConfigNode node) ||
                !(node is ConfigStringNode text) || string.IsNullOrWhiteSpace(text.Value))
            {
                throw new InvalidDataException(path + "." + property + " must be a non-empty string.");
            }

            return text.Value;
        }

        private static long RequireInteger(ConfigObjectNode value, string property, string path)
        {
            if (!value.TryGetValue(property, out ConfigNode node) || !(node is ConfigIntegerNode integer))
            {
                throw new InvalidDataException(path + "." + property + " must be an integer.");
            }

            return integer.Value;
        }

        private static IReadOnlyList<string> ReadStrings(ConfigArrayNode values, string path)
        {
            var result = new List<string>();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConfigNode node in values.Items)
            {
                if (!(node is ConfigStringNode text) || string.IsNullOrWhiteSpace(text.Value) || !unique.Add(text.Value))
                {
                    throw new InvalidDataException(path + " must contain unique non-empty strings.");
                }

                result.Add(text.Value);
            }

            return result;
        }

        private static IReadOnlyList<ConfigAuthoringSheetProfile> ReadAuthoringSheets(
            ConfigObjectNode workbook,
            IReadOnlyList<string> workbookTables,
            string path)
        {
            if (!workbook.TryGetValue("authoringSheets", out ConfigNode node))
            {
                return Array.Empty<ConfigAuthoringSheetProfile>();
            }

            if (!(node is ConfigArrayNode array) || array.Items.Count == 0)
            {
                throw new InvalidDataException(path + ".authoringSheets must be a non-empty array.");
            }

            var sheets = new List<ConfigAuthoringSheetProfile>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var assignedTables = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConfigNode sheetNode in array.Items)
            {
                ConfigObjectNode sheet = RequireObject(
                    sheetNode,
                    path + ".authoringSheets[]",
                    AuthoringSheetKeys);
                string name = RequireString(sheet, "name", path + ".authoringSheets[]");
                ValidateWorksheetName(name, path + ".authoringSheets[].name");
                if (!names.Add(name))
                {
                    throw new InvalidDataException("Duplicate authoring Sheet name: " + name);
                }

                IReadOnlyList<string> tables = ReadStrings(
                    RequireArray(sheet, "tables", path + ".authoringSheets[]"),
                    path + ".authoringSheets[].tables");
                foreach (string table in tables)
                {
                    if (!workbookTables.Contains(table) || !assignedTables.Add(table))
                    {
                        throw new InvalidDataException(
                            "Authoring Sheet tables must each belong to the workbook exactly once: " + table);
                    }
                }

                sheets.Add(new ConfigAuthoringSheetProfile(name, tables));
            }

            if (!assignedTables.SetEquals(workbookTables))
            {
                throw new InvalidDataException(
                    "authoringSheets must assign every workbook table exactly once.");
            }

            return sheets;
        }

        private static void ValidateWorksheetName(string value, string path)
        {
            if (value.Length > 31 || value.IndexOfAny(new[] { '[', ']', ':', '*', '?', '/', '\\' }) >= 0 ||
                value.StartsWith("_zgs_", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, XlsxConfigWorkbookWriter.NavigationSheetName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(path + " is not a valid authoring Sheet name.");
            }
        }

        private static string OptionalPath(ConfigObjectNode value, string property)
        {
            return value.TryGetValue(property, out ConfigNode node)
                ? NormalizePath((node as ConfigStringNode)?.Value ?? throw new InvalidDataException(property + " must be a string."))
                : null;
        }

        private static string NormalizePath(string value)
        {
            return ConfigPathGuard.NormalizeRelativePath(value);
        }

        private static HashSet<string> Keys(params string[] values)
        {
            return new HashSet<string>(values, StringComparer.Ordinal);
        }
    }
}
