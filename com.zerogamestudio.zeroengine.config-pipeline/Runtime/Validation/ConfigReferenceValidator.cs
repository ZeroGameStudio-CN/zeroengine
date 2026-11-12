using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
            var compositePrimaryKeys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var references = new List<ReferenceValue>();
            Collect(
                schema.Root,
                document.Root,
                "#",
                "$",
                document,
                primaryKeys,
                compositePrimaryKeys,
                references,
                diagnostics,
                primaryKeyHandledByParent: false);

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
            Dictionary<string, HashSet<string>> compositePrimaryKeys,
            List<ReferenceValue> references,
            List<ConfigDiagnostic> diagnostics,
            bool primaryKeyHandledByParent)
        {
            if (schemaNode.PrimaryKey && !primaryKeyHandledByParent)
            {
                CollectStandalonePrimaryKey(
                    dataNode,
                    schemaPath,
                    dataPath,
                    document,
                    primaryKeys,
                    diagnostics);
            }

            if (!string.IsNullOrEmpty(schemaNode.ReferencePath))
            {
                if (dataNode is ConfigStringNode referenceValue)
                {
                    if (referenceValue.Value.Length != 0 || string.IsNullOrEmpty(schemaNode.PresetType))
                    {
                        references.Add(new ReferenceValue(
                            schemaNode.ReferencePath,
                            referenceValue.Value,
                            dataPath));
                    }
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
                CollectObjectPrimaryKey(
                    schemaNode,
                    dataObject,
                    schemaPath,
                    dataPath,
                    document,
                    primaryKeys,
                    compositePrimaryKeys,
                    diagnostics);

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
                        compositePrimaryKeys,
                        references,
                        diagnostics,
                        primaryKeyHandledByParent: property.Schema.PrimaryKey);
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
                        compositePrimaryKeys,
                        references,
                        diagnostics,
                        primaryKeyHandledByParent: false);
                }
            }
        }

        private static void CollectStandalonePrimaryKey(
            ConfigNode dataNode,
            string schemaPath,
            string dataPath,
            ConfigDocument document,
            Dictionary<string, HashSet<string>> primaryKeys,
            List<ConfigDiagnostic> diagnostics)
        {
            if (!(dataNode is ConfigStringNode primaryValue))
            {
                diagnostics.Add(Error(
                    "CONFIG_PRIMARY_KEY_TYPE",
                    document,
                    dataPath,
                    "Primary keys must be strings."));
                return;
            }

            if (!primaryKeys.TryGetValue(schemaPath, out HashSet<string> values))
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

        private static void CollectObjectPrimaryKey(
            ConfigSchemaNode schemaNode,
            ConfigObjectNode dataObject,
            string schemaPath,
            string dataPath,
            ConfigDocument document,
            Dictionary<string, HashSet<string>> primaryKeys,
            Dictionary<string, HashSet<string>> compositePrimaryKeys,
            List<ConfigDiagnostic> diagnostics)
        {
            ConfigSchemaProperty[] keyProperties = schemaNode.Properties
                .Where(property => property.Schema.PrimaryKey)
                .ToArray();
            if (keyProperties.Length == 0)
            {
                return;
            }

            var keyValues = new string[keyProperties.Length];
            bool valid = true;
            for (int index = 0; index < keyProperties.Length; index++)
            {
                ConfigSchemaProperty keyProperty = keyProperties[index];
                string keyDataPath = dataPath + "/" + EscapePointer(keyProperty.Name);
                if (!dataObject.TryGetValue(keyProperty.Name, out ConfigNode keyNode))
                {
                    diagnostics.Add(Error(
                        "CONFIG_PRIMARY_KEY_MISSING",
                        document,
                        keyDataPath,
                        "Primary key component '" + keyProperty.Name + "' is required."));
                    valid = false;
                }
                else if (!(keyNode is ConfigStringNode keyValue))
                {
                    diagnostics.Add(Error(
                        "CONFIG_PRIMARY_KEY_TYPE",
                        document,
                        keyDataPath,
                        "Primary keys must be strings."));
                    valid = false;
                }
                else
                {
                    keyValues[index] = keyValue.Value;
                }
            }

            if (!valid)
            {
                return;
            }

            if (keyProperties.Length == 1)
            {
                string keySchemaPath = schemaPath + "/properties/" + EscapePointer(keyProperties[0].Name);
                if (!primaryKeys.TryGetValue(keySchemaPath, out HashSet<string> values))
                {
                    values = new HashSet<string>(StringComparer.Ordinal);
                    primaryKeys.Add(keySchemaPath, values);
                }

                if (!values.Add(keyValues[0]))
                {
                    diagnostics.Add(Error(
                        "CONFIG_PRIMARY_KEY_DUPLICATE",
                        document,
                        dataPath + "/" + EscapePointer(keyProperties[0].Name),
                        "Duplicate primary key '" + keyValues[0] + "'."));
                }

                return;
            }

            if (!compositePrimaryKeys.TryGetValue(schemaPath, out HashSet<string> identities))
            {
                identities = new HashSet<string>(StringComparer.Ordinal);
                compositePrimaryKeys.Add(schemaPath, identities);
            }

            if (!identities.Add(CreateCompositePrimaryKeyIdentity(keyValues)))
            {
                string identity = string.Join(
                    ", ",
                    keyProperties.Select((property, index) =>
                        property.Name + "='" + keyValues[index] + "'"));
                diagnostics.Add(Error(
                    "CONFIG_PRIMARY_KEY_DUPLICATE",
                    document,
                    dataPath,
                    "Duplicate composite primary key (" + identity + ")."));
            }
        }

        private static string CreateCompositePrimaryKeyIdentity(IReadOnlyList<string> values)
        {
            var builder = new StringBuilder();
            foreach (string value in values)
            {
                builder.Append(value.Length).Append(':').Append(value);
            }

            return builder.ToString();
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
