using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeroGameStudio.ConfigPipeline.Editor
{
    public static class WorkshopSchemaProjector
    {
        public static ConfigObjectNode Project(ConfigSchema schema, bool relaxRequired)
        {
            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            return (ConfigObjectNode)ProjectNode(schema.SourceNode, relaxRequired, false);
        }

        private static ConfigNode ProjectNode(
            ConfigNode node,
            bool relaxRequired,
            bool propertiesContainer)
        {
            if (node is ConfigObjectNode configObject)
            {
                HashSet<string> excludedProperties = ExcludedPropertyNames(configObject);
                var properties = new List<ConfigProperty>();
                foreach (ConfigProperty property in configObject.Properties)
                {
                    if (property.Name.StartsWith("x-zgs-", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (relaxRequired && property.Name == "required")
                    {
                        continue;
                    }

                    if (property.Name == "required" &&
                        property.Value is ConfigArrayNode required &&
                        excludedProperties.Count != 0)
                    {
                        properties.Add(new ConfigProperty(
                            property.Name,
                            new ConfigArrayNode(required.Items.Where(item =>
                                !(item is ConfigStringNode name) ||
                                !excludedProperties.Contains(name.Value)))));
                        continue;
                    }

                    if (propertiesContainer &&
                        property.Value is ConfigObjectNode fieldSchema &&
                        IsExcludedField(fieldSchema))
                    {
                        continue;
                    }

                    properties.Add(new ConfigProperty(
                        property.Name,
                        ProjectNode(
                            property.Value,
                            relaxRequired,
                            property.Name == "properties")));
                }

                return new ConfigObjectNode(properties);
            }

            if (node is ConfigArrayNode array)
            {
                var items = new List<ConfigNode>();
                foreach (ConfigNode item in array.Items)
                {
                    items.Add(ProjectNode(item, relaxRequired, false));
                }

                return new ConfigArrayNode(items);
            }

            return node;
        }

        private static HashSet<string> ExcludedPropertyNames(ConfigObjectNode schemaNode)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (!schemaNode.TryGetValue("properties", out ConfigNode propertiesNode) ||
                !(propertiesNode is ConfigObjectNode properties))
            {
                return result;
            }

            foreach (ConfigProperty property in properties.Properties)
            {
                if (property.Value is ConfigObjectNode fieldSchema &&
                    IsExcludedField(fieldSchema))
                {
                    result.Add(property.Name);
                }
            }

            return result;
        }

        private static bool IsExcludedField(ConfigObjectNode fieldSchema)
        {
            if (fieldSchema.TryGetValue(
                    "x-zgs-authoring-only",
                    out ConfigNode authoringOnly) &&
                authoringOnly is ConfigBooleanNode authoring &&
                authoring.Value)
            {
                return true;
            }

            return fieldSchema.TryGetValue("x-zgs-scope", out ConfigNode scope) &&
                   scope is ConfigStringNode scopeText &&
                   string.Equals(scopeText.Value, "server", StringComparison.Ordinal);
        }
    }
}
