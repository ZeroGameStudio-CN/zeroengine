using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ZeroEngine.TCE;

namespace ZeroEngine.TCE.ModSystem
{
    public static class TceModGraphJsonParser
    {
        public static bool TryParse(
            string json,
            string path,
            out TceExternalGraphDocument document,
            out IReadOnlyList<TceValidationIssue> issues)
        {
            document = null;
            var issueList = new List<TceValidationIssue>();
            string issuePath = string.IsNullOrWhiteSpace(path) ? "graph" : path;

            if (string.IsNullOrWhiteSpace(json))
            {
                issueList.Add(CreateIssue(issuePath, "Graph JSON must not be empty."));
                issues = issueList;
                return false;
            }

            JObject root;
            try
            {
                root = JToken.Parse(json) as JObject;
            }
            catch (JsonException ex)
            {
                issueList.Add(CreateIssue(issuePath, ex.Message));
                issues = issueList;
                return false;
            }

            if (root == null)
            {
                issueList.Add(CreateIssue(issuePath, "Graph JSON root must be an object."));
                issues = issueList;
                return false;
            }

            if (ContainsForbiddenTypeHint(root, issuePath, issueList))
            {
                issues = issueList;
                return false;
            }

            document = new TceExternalGraphDocument
            {
                Format = ReadString(root, "format"),
                SchemaVersion = ReadInt(root, "schemaVersion"),
                GraphId = ReadString(root, "graphId"),
                DisplayName = ReadString(root, "displayName")
            };

            ReadLane(root, "triggers", document.Triggers, issueList);
            ReadLane(root, "conditions", document.Conditions, issueList);
            ReadLane(root, "effects", document.Effects, issueList);

            issues = issueList;
            if (issueList.Count == 0)
                return true;

            document = null;
            return false;
        }

        private static void ReadLane(JObject root, string laneName, List<TceExternalGraphNode> destination, List<TceValidationIssue> issues)
        {
            JToken laneToken = root[laneName];
            if (laneToken == null || laneToken.Type == JTokenType.Null)
                return;

            if (laneToken is not JArray lane)
            {
                issues.Add(CreateIssue(laneName, $"{laneName} must be an array."));
                return;
            }

            for (int i = 0; i < lane.Count; i++)
            {
                string nodePath = $"{laneName}[{i}]";
                if (lane[i] is not JObject node)
                {
                    issues.Add(CreateIssue(nodePath, "Graph node must be an object."));
                    continue;
                }

                var fields = new Dictionary<string, object>(StringComparer.Ordinal);
                if (node["fields"] is JObject fieldObject)
                {
                    foreach (JProperty field in fieldObject.Properties())
                        fields[field.Name] = ConvertFieldValue(field.Value);
                }
                else if (node["fields"] != null && node["fields"].Type != JTokenType.Null)
                {
                    issues.Add(CreateIssue($"{nodePath}.fields", "Node fields must be an object."));
                    continue;
                }

                destination.Add(new TceExternalGraphNode(ReadString(node, "componentId"), fields));
            }
        }

        private static object ConvertFieldValue(JToken token)
        {
            return token.Type switch
            {
                JTokenType.String => token.Value<string>(),
                JTokenType.Integer => token.Value<long>(),
                JTokenType.Float => token.Value<double>(),
                JTokenType.Boolean => token.Value<bool>(),
                _ => token.ToString(Formatting.None)
            };
        }

        private static bool ContainsForbiddenTypeHint(JToken token, string path, List<TceValidationIssue> issues)
        {
            if (token is JObject obj)
            {
                foreach (JProperty property in obj.Properties())
                {
                    string propertyPath = string.IsNullOrEmpty(path) ? property.Name : $"{path}.{property.Name}";
                    if (property.Name == "$type" || property.Name == "managedReferenceFullTypename")
                    {
                        issues.Add(CreateIssue(propertyPath, "TCE graph JSON must not contain CLR or Unity managed-reference type hints."));
                        return true;
                    }

                    if (ContainsForbiddenTypeHint(property.Value, propertyPath, issues))
                        return true;
                }
            }
            else if (token is JArray array)
            {
                for (int i = 0; i < array.Count; i++)
                {
                    if (ContainsForbiddenTypeHint(array[i], $"{path}[{i}]", issues))
                        return true;
                }
            }
            else if (token.Type == JTokenType.String &&
                     token.Value<string>()?.IndexOf("Assembly-CSharp", StringComparison.Ordinal) >= 0)
            {
                issues.Add(CreateIssue(path, "TCE graph JSON must not contain project assembly type names."));
                return true;
            }

            return false;
        }

        private static string ReadString(JObject obj, string propertyName)
        {
            return obj[propertyName]?.Type == JTokenType.String
                ? obj[propertyName].Value<string>()
                : string.Empty;
        }

        private static int ReadInt(JObject obj, string propertyName)
        {
            return obj[propertyName]?.Type == JTokenType.Integer
                ? obj[propertyName].Value<int>()
                : 0;
        }

        private static TceValidationIssue CreateIssue(string path, string message)
        {
            return new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.InvalidField, path, message);
        }
    }
}
