using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ZeroGameStudio.ConfigPipeline
{
    public static class ConfigSchemaNormalizer
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

        public static ConfigNormalizationResult Normalize(
            ConfigDocument source,
            ConfigSchema schema,
            string targetScope)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            ConfigFieldScope parsedTargetScope = ParseTargetScope(targetScope);
            var diagnostics = new List<ConfigDiagnostic>();
            if (!string.Equals(source.SchemaId, schema.SchemaId, StringComparison.Ordinal))
            {
                AddError(
                    diagnostics,
                    "CONFIG_SCHEMA_ID_MISMATCH",
                    source,
                    "$",
                    "Document schema ID does not match the selected schema.");
            }

            if (source.SchemaVersion != schema.SchemaVersion)
            {
                AddError(
                    diagnostics,
                    "CONFIG_SCHEMA_VERSION_MISMATCH",
                    source,
                    "$",
                    "Document schema version does not match the selected schema.");
            }

            ConfigNode normalized = NormalizeNode(
                source.Root,
                schema.Root,
                source,
                "$",
                parsedTargetScope,
                diagnostics,
                true);
            if (HasErrors(diagnostics) || !(normalized is ConfigObjectNode normalizedRoot))
            {
                return new ConfigNormalizationResult(null, diagnostics);
            }

            return new ConfigNormalizationResult(
                new ConfigDocument(
                    source.ConfigSetId,
                    source.SchemaId,
                    source.SchemaVersion,
                    normalizedRoot),
                diagnostics);
        }

        private static ConfigNode NormalizeNode(
            ConfigNode value,
            ConfigSchemaNode schema,
            ConfigDocument document,
            string path,
            ConfigFieldScope targetScope,
            List<ConfigDiagnostic> diagnostics,
            bool checkEnum)
        {
            ConfigNode normalized;
            switch (schema.Type)
            {
                case ConfigSchemaType.Object:
                    normalized = NormalizeObject(
                        value,
                        schema,
                        document,
                        path,
                        targetScope,
                        diagnostics);
                    break;
                case ConfigSchemaType.Array:
                    normalized = NormalizeArray(
                        value,
                        schema,
                        document,
                        path,
                        targetScope,
                        diagnostics);
                    break;
                case ConfigSchemaType.String:
                    normalized = NormalizeString(value, schema, document, path, diagnostics);
                    break;
                case ConfigSchemaType.Integer:
                    normalized = NormalizeInteger(value, schema, document, path, diagnostics);
                    break;
                case ConfigSchemaType.Number:
                    normalized = NormalizeNumber(value, schema, document, path, diagnostics);
                    break;
                case ConfigSchemaType.Boolean:
                    normalized = value is ConfigBooleanNode
                        ? value
                        : TypeError(document, path, "boolean", diagnostics);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (normalized != null &&
                checkEnum &&
                schema.EnumValues.Count != 0 &&
                !MatchesEnum(
                    normalized,
                    schema,
                    document,
                    path,
                    targetScope,
                    diagnostics))
            {
                AddError(
                    diagnostics,
                    "CONFIG_ENUM_INVALID",
                    document,
                    path,
                    "Value is not one of the declared enum values.");
                return null;
            }

            return normalized;
        }

        private static ConfigNode NormalizeObject(
            ConfigNode value,
            ConfigSchemaNode schema,
            ConfigDocument document,
            string path,
            ConfigFieldScope targetScope,
            List<ConfigDiagnostic> diagnostics)
        {
            if (!(value is ConfigObjectNode sourceObject))
            {
                return TypeError(document, path, "object", diagnostics);
            }

            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConfigSchemaProperty property in schema.Properties)
            {
                declared.Add(property.Name);
            }

            foreach (ConfigProperty property in sourceObject.Properties)
            {
                if (!declared.Contains(property.Name))
                {
                    AddError(
                        diagnostics,
                        "CONFIG_UNKNOWN_PROPERTY",
                        document,
                        path + "/" + EscapePath(property.Name),
                        "Unknown property '" + property.Name + "'.");
                }
            }

            var normalizedProperties = new List<ConfigProperty>();
            foreach (ConfigSchemaProperty property in schema.Properties)
            {
                ConfigSchemaNode propertySchema = property.Schema;
                if (propertySchema.AuthoringOnly ||
                    !IncludesScope(propertySchema.Scope, targetScope))
                {
                    continue;
                }

                string propertyPath = path + "/" + EscapePath(property.Name);
                if (!sourceObject.TryGetValue(property.Name, out ConfigNode sourceValue))
                {
                    if (propertySchema.DefaultValue != null)
                    {
                        ConfigNode normalizedDefault = NormalizeNode(
                            propertySchema.DefaultValue,
                            propertySchema,
                            document,
                            propertyPath,
                            targetScope,
                            diagnostics,
                            true);
                        if (normalizedDefault != null)
                        {
                            normalizedProperties.Add(
                                new ConfigProperty(property.Name, normalizedDefault));
                        }
                    }
                    else if (schema.IsRequired(property.Name))
                    {
                        AddError(
                            diagnostics,
                            "CONFIG_REQUIRED_MISSING",
                            document,
                            propertyPath,
                            "Required property is missing.");
                    }

                    continue;
                }

                ConfigNode normalizedValue = NormalizeNode(
                    sourceValue,
                    propertySchema,
                    document,
                    propertyPath,
                    targetScope,
                    diagnostics,
                    true);
                if (normalizedValue != null)
                {
                    normalizedProperties.Add(new ConfigProperty(property.Name, normalizedValue));
                }
            }

            return new ConfigObjectNode(normalizedProperties);
        }

        private static ConfigNode NormalizeArray(
            ConfigNode value,
            ConfigSchemaNode schema,
            ConfigDocument document,
            string path,
            ConfigFieldScope targetScope,
            List<ConfigDiagnostic> diagnostics)
        {
            if (!(value is ConfigArrayNode sourceArray))
            {
                return TypeError(document, path, "array", diagnostics);
            }

            if (schema.MinItems.HasValue && sourceArray.Items.Count < schema.MinItems.Value)
            {
                AddError(
                    diagnostics,
                    "CONFIG_ARRAY_TOO_SHORT",
                    document,
                    path,
                    "Array contains fewer than minItems entries.");
            }

            if (schema.MaxItems.HasValue && sourceArray.Items.Count > schema.MaxItems.Value)
            {
                AddError(
                    diagnostics,
                    "CONFIG_ARRAY_TOO_LONG",
                    document,
                    path,
                    "Array contains more than maxItems entries.");
            }

            var normalizedItems = new List<ConfigNode>();
            var uniqueValues = schema.UniqueItems
                ? new HashSet<string>(StringComparer.Ordinal)
                : null;
            for (int index = 0; index < sourceArray.Items.Count; index++)
            {
                string itemPath = path + "/" + index.ToString(CultureInfo.InvariantCulture);
                ConfigNode normalizedItem = NormalizeNode(
                    sourceArray.Items[index],
                    schema.Items,
                    document,
                    itemPath,
                    targetScope,
                    diagnostics,
                    true);
                if (normalizedItem == null)
                {
                    continue;
                }

                normalizedItems.Add(normalizedItem);
                if (uniqueValues != null &&
                    !uniqueValues.Add(CanonicalJsonWriter.WriteText(normalizedItem)))
                {
                    AddError(
                        diagnostics,
                        "CONFIG_ARRAY_DUPLICATE",
                        document,
                        itemPath,
                        "Array items must be unique.");
                }
            }

            return new ConfigArrayNode(normalizedItems);
        }

        private static ConfigNode NormalizeString(
            ConfigNode value,
            ConfigSchemaNode schema,
            ConfigDocument document,
            string path,
            List<ConfigDiagnostic> diagnostics)
        {
            if (!(value is ConfigStringNode stringValue))
            {
                return TypeError(document, path, "string", diagnostics);
            }

            if ((schema.PrimaryKey ||
                 !string.IsNullOrEmpty(schema.ReferencePath) ||
                 !string.IsNullOrEmpty(schema.AssetType) ||
                 schema.LocalizationKey) &&
                (stringValue.Value.Length == 0 ||
                 !string.Equals(stringValue.Value, stringValue.Value.Trim(), StringComparison.Ordinal)))
            {
                AddError(
                    diagnostics,
                    "CONFIG_STABLE_ID_INVALID",
                    document,
                    path,
                    "Identity and reference strings cannot be empty or contain edge whitespace.");
                return null;
            }

            if (!string.IsNullOrEmpty(schema.Pattern))
            {
                try
                {
                    if (!Regex.IsMatch(
                            stringValue.Value,
                            schema.Pattern,
                            RegexOptions.CultureInvariant,
                            RegexTimeout))
                    {
                        AddError(
                            diagnostics,
                            "CONFIG_PATTERN_MISMATCH",
                            document,
                            path,
                            "String does not match the declared pattern.");
                        return null;
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    AddError(
                        diagnostics,
                        "CONFIG_PATTERN_TIMEOUT",
                        document,
                        path,
                        "Pattern validation exceeded the fixed timeout.");
                    return null;
                }
            }

            return stringValue;
        }

        private static ConfigNode NormalizeInteger(
            ConfigNode value,
            ConfigSchemaNode schema,
            ConfigDocument document,
            string path,
            List<ConfigDiagnostic> diagnostics)
        {
            if (!(value is ConfigIntegerNode integerValue))
            {
                return TypeError(document, path, "integer", diagnostics);
            }

            long integer = integerValue.Value;
            if (schema.IntegerType == ConfigIntegerType.Int32 &&
                (integer < int.MinValue || integer > int.MaxValue))
            {
                AddError(
                    diagnostics,
                    "CONFIG_INT32_RANGE",
                    document,
                    path,
                    "Integer is outside the int32 range.");
                return null;
            }

            if (!ValidateRange(integer, schema, document, path, diagnostics))
            {
                return null;
            }

            return integerValue;
        }

        private static ConfigNode NormalizeNumber(
            ConfigNode value,
            ConfigSchemaNode schema,
            ConfigDocument document,
            string path,
            List<ConfigDiagnostic> diagnostics)
        {
            double rawValue;
            if (value is ConfigIntegerNode integerValue)
            {
                rawValue = integerValue.Value;
            }
            else if (value is ConfigNumberNode numberValue)
            {
                rawValue = numberValue.Value;
            }
            else
            {
                return TypeError(document, path, "number", diagnostics);
            }

            ConfigNumberNode normalized = schema.NumberType == ConfigNumberType.Float32
                ? new ConfigNumberNode((float)rawValue)
                : new ConfigNumberNode(rawValue);
            if (!ValidateRange(normalized.Value, schema, document, path, diagnostics))
            {
                return null;
            }

            return normalized;
        }

        private static bool ValidateRange(
            double value,
            ConfigSchemaNode schema,
            ConfigDocument document,
            string path,
            List<ConfigDiagnostic> diagnostics)
        {
            if ((schema.Minimum.HasValue && value < schema.Minimum.Value) ||
                (schema.ExclusiveMinimum.HasValue && value <= schema.ExclusiveMinimum.Value))
            {
                AddError(
                    diagnostics,
                    "CONFIG_MINIMUM",
                    document,
                    path,
                    "Number is below the declared minimum.");
                return false;
            }

            if ((schema.Maximum.HasValue && value > schema.Maximum.Value) ||
                (schema.ExclusiveMaximum.HasValue && value >= schema.ExclusiveMaximum.Value))
            {
                AddError(
                    diagnostics,
                    "CONFIG_MAXIMUM",
                    document,
                    path,
                    "Number exceeds the declared maximum.");
                return false;
            }

            return true;
        }

        private static bool MatchesEnum(
            ConfigNode normalized,
            ConfigSchemaNode schema,
            ConfigDocument document,
            string path,
            ConfigFieldScope targetScope,
            List<ConfigDiagnostic> diagnostics)
        {
            string actual = CanonicalJsonWriter.WriteText(normalized);
            foreach (ConfigNode enumValue in schema.EnumValues)
            {
                var enumDiagnostics = new List<ConfigDiagnostic>();
                ConfigNode normalizedEnum = NormalizeNode(
                    enumValue,
                    schema,
                    document,
                    path,
                    targetScope,
                    enumDiagnostics,
                    false);
                if (normalizedEnum != null &&
                    !HasErrors(enumDiagnostics) &&
                    string.Equals(
                        actual,
                        CanonicalJsonWriter.WriteText(normalizedEnum),
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static ConfigNode TypeError(
            ConfigDocument document,
            string path,
            string expectedType,
            List<ConfigDiagnostic> diagnostics)
        {
            AddError(
                diagnostics,
                "CONFIG_TYPE_MISMATCH",
                document,
                path,
                "Expected " + expectedType + ".");
            return null;
        }

        private static ConfigFieldScope ParseTargetScope(string scope)
        {
            switch (scope)
            {
                case "shared":
                    return ConfigFieldScope.Shared;
                case "client":
                    return ConfigFieldScope.Client;
                case "server":
                    return ConfigFieldScope.Server;
                default:
                    throw new ArgumentException(
                        "Target scope must be shared, client, or server.",
                        nameof(scope));
            }
        }

        private static bool IncludesScope(ConfigFieldScope fieldScope, ConfigFieldScope targetScope)
        {
            return fieldScope == ConfigFieldScope.Shared || fieldScope == targetScope;
        }

        private static bool HasErrors(IEnumerable<ConfigDiagnostic> diagnostics)
        {
            foreach (ConfigDiagnostic diagnostic in diagnostics)
            {
                if (diagnostic.Severity == ConfigDiagnosticSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddError(
            ICollection<ConfigDiagnostic> diagnostics,
            string code,
            ConfigDocument document,
            string path,
            string message)
        {
            diagnostics.Add(new ConfigDiagnostic(
                code,
                ConfigDiagnosticSeverity.Error,
                message,
                document.ConfigSetId,
                path));
        }

        private static string EscapePath(string value)
        {
            return value.Replace("~", "~0").Replace("/", "~1");
        }
    }
}
