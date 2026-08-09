using System;
using System.Collections.Generic;

namespace ZeroGameStudio.ConfigPipeline
{
    public sealed class ConfigReferenceValidator : IConfigValidator
    {
        private readonly ConfigSchema schema;

        public ConfigReferenceValidator(ConfigSchema schema)
        {
            this.schema = schema ?? throw new ArgumentNullException(nameof(schema));
        }

        public IReadOnlyList<ConfigDiagnostic> Validate(
            ConfigDocument document,
            ConfigValidationContext context)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var diagnostics = new List<ConfigDiagnostic>();
            var primaryKeys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var references = new List<ReferenceValue>();
            Collect(
                schema.Root,
                document.Root,
                "#",
                "$",
                document,
                primaryKeys,
                references,
                diagnostics);

            foreach (ReferenceValue reference in references)
            {
                if (!primaryKeys.TryGetValue(reference.TargetSchemaPath, out HashSet<string> targetValues))
                {
                    diagnostics.Add(Error(
                        "CONFIG_REFERENCE_TARGET_INVALID",
                        document,
                        reference.DataPath,
                        "Reference target '" + reference.TargetSchemaPath +
                        "' is not a declared primary key."));
                }
                else if (!targetValues.Contains(reference.Value))
                {
                    diagnostics.Add(Error(
                        "CONFIG_REFERENCE_DANGLING",
                        document,
                        reference.DataPath,
                        "Reference value '" + reference.Value + "' does not exist."));
                }
            }

            return diagnostics;
        }

        private static void Collect(
            ConfigSchemaNode schemaNode,
            ConfigNode dataNode,
            string schemaPath,
            string dataPath,
            ConfigDocument document,
            Dictionary<string, HashSet<string>> primaryKeys,
            List<ReferenceValue> references,
            List<ConfigDiagnostic> diagnostics)
        {
            if (schemaNode.PrimaryKey)
            {
                if (!(dataNode is ConfigStringNode primaryValue))
                {
                    diagnostics.Add(Error(
                        "CONFIG_PRIMARY_KEY_TYPE",
                        document,
                        dataPath,
                        "Primary keys must be strings."));
                }
                else
                {
                    if (!primaryKeys.TryGetValue(
                            schemaPath,
                            out HashSet<string> values))
                    {
                        values = new HashSet<string>(StringComparer.Ordinal);
                        primaryKeys.Add(schemaPath, values);
                    }

                    if (!values.Add(primaryValue.Value))
                    {
                        diagnostics.Add(Error(
                            "CONFIG_PRIMARY_KEY_DUPLICATE",
                            document,
                            dataPath,
                            "Duplicate primary key '" + primaryValue.Value + "'."));
                    }
                }
            }

            if (!string.IsNullOrEmpty(schemaNode.ReferencePath))
            {
                if (dataNode is ConfigStringNode referenceValue)
                {
                    references.Add(new ReferenceValue(
                        schemaNode.ReferencePath,
                        referenceValue.Value,
                        dataPath));
                }
                else
                {
                    diagnostics.Add(Error(
                        "CONFIG_REFERENCE_TYPE",
                        document,
                        dataPath,
                        "References must be strings."));
                }
            }

            if (schemaNode.Type == ConfigSchemaType.Object &&
                dataNode is ConfigObjectNode dataObject)
            {
                foreach (ConfigSchemaProperty property in schemaNode.Properties)
                {
                    if (!dataObject.TryGetValue(property.Name, out ConfigNode propertyValue))
                    {
                        continue;
                    }

                    Collect(
                        property.Schema,
                        propertyValue,
                        schemaPath + "/properties/" + EscapePointer(property.Name),
                        dataPath + "/" + EscapePointer(property.Name),
                        document,
                        primaryKeys,
                        references,
                        diagnostics);
                }
            }
            else if (schemaNode.Type == ConfigSchemaType.Array &&
                     dataNode is ConfigArrayNode dataArray)
            {
                for (int index = 0; index < dataArray.Items.Count; index++)
                {
                    Collect(
                        schemaNode.Items,
                        dataArray.Items[index],
                        schemaPath + "/items",
                        dataPath + "/" + index,
                        document,
                        primaryKeys,
                        references,
                        diagnostics);
                }
            }
        }

        private static ConfigDiagnostic Error(
            string code,
            ConfigDocument document,
            string path,
            string message)
        {
            return new ConfigDiagnostic(
                code,
                ConfigDiagnosticSeverity.Error,
                message,
                document.ConfigSetId,
                path);
        }

        private static string EscapePointer(string value)
        {
            return value.Replace("~", "~0").Replace("/", "~1");
        }

        private sealed class ReferenceValue
        {
            public ReferenceValue(string targetSchemaPath, string value, string dataPath)
            {
                TargetSchemaPath = targetSchemaPath;
                Value = value;
                DataPath = dataPath;
            }

            public string TargetSchemaPath { get; }

            public string Value { get; }

            public string DataPath { get; }
        }
    }
}
