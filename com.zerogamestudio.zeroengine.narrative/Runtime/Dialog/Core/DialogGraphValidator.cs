using System;
using System.Collections.Generic;

namespace ZeroEngine.Dialog
{
    public sealed class DialogGraphValidationOptions
    {
        public static DialogGraphValidationOptions Default { get; } = new DialogGraphValidationOptions();

        public Func<string, bool> IsKnownLocalizationKey { get; set; }
    }

    public static class DialogGraphValidator
    {
        public static List<DialogGraphValidationIssue> Validate(
            DialogGraphSO graph,
            DialogGraphValidationOptions options = null)
        {
            options ??= DialogGraphValidationOptions.Default;
            var issues = new List<DialogGraphValidationIssue>();
            if (graph == null)
            {
                issues.Add(new DialogGraphValidationIssue(
                    DialogGraphValidationSeverity.Error,
                    DialogGraphValidationCodes.MissingStartNode,
                    message: "Graph is null."));
                return issues;
            }

            var nodeById = new Dictionary<string, DialogNode>();
            var hasStart = false;
            var hasEnd = false;

            foreach (var node in graph.Nodes)
            {
                if (node == null)
                {
                    continue;
                }

                hasStart |= node.Type == DialogNodeType.Start;
                hasEnd |= node.Type == DialogNodeType.End;

                if (string.IsNullOrEmpty(node.Id))
                {
                    continue;
                }

                if (nodeById.ContainsKey(node.Id))
                {
                    issues.Add(new DialogGraphValidationIssue(
                        DialogGraphValidationSeverity.Error,
                        DialogGraphValidationCodes.DuplicateNodeId,
                        graph.DisplayName,
                        node.Id,
                        message: $"Duplicate node ID: '{node.Id}'"));
                    continue;
                }

                nodeById.Add(node.Id, node);
            }

            if (!hasStart)
            {
                issues.Add(new DialogGraphValidationIssue(
                    DialogGraphValidationSeverity.Error,
                    DialogGraphValidationCodes.MissingStartNode,
                    graph.DisplayName,
                    message: "Graph has no Start node."));
            }

            if (!hasEnd)
            {
                issues.Add(new DialogGraphValidationIssue(
                    DialogGraphValidationSeverity.Error,
                    DialogGraphValidationCodes.MissingEndNode,
                    graph.DisplayName,
                    message: "Graph has no End node."));
            }

            foreach (var node in graph.Nodes)
            {
                if (node == null)
                {
                    continue;
                }

                foreach (var outputId in node.GetOutputNodeIds())
                {
                    if (string.IsNullOrEmpty(outputId) || outputId == "__END__")
                    {
                        continue;
                    }

                    if (!nodeById.ContainsKey(outputId))
                    {
                        issues.Add(new DialogGraphValidationIssue(
                            DialogGraphValidationSeverity.Error,
                            DialogGraphValidationCodes.BrokenOutputConnection,
                            graph.DisplayName,
                            node.Id,
                            outputId,
                            message: $"Node '{node.Id}' references non-existent node '{outputId}'."));
                    }
                }

                if (node is DialogCallbackNode callbackNode &&
                    !string.IsNullOrEmpty(callbackNode.CallbackId) &&
                    !DialogCommandParser.TryParse(callbackNode.CallbackId, callbackNode.Parameter, out _))
                {
                    issues.Add(new DialogGraphValidationIssue(
                        DialogGraphValidationSeverity.Warning,
                        DialogGraphValidationCodes.UnknownCommandId,
                        graph.DisplayName,
                        node.Id,
                        commandId: callbackNode.CallbackId,
                        message: $"Unknown dialog command '{callbackNode.CallbackId}'."));
                }

                if (node is DialogTextNode textNode &&
                    !string.IsNullOrEmpty(textNode.LocalizationKey) &&
                    options.IsKnownLocalizationKey != null &&
                    !options.IsKnownLocalizationKey(textNode.LocalizationKey))
                {
                    issues.Add(new DialogGraphValidationIssue(
                        DialogGraphValidationSeverity.Warning,
                        DialogGraphValidationCodes.UnknownLocalizationKey,
                        graph.DisplayName,
                        node.Id,
                        message: $"Unknown localization key '{textNode.LocalizationKey}'."));
                }
            }

            return issues;
        }
    }
}
