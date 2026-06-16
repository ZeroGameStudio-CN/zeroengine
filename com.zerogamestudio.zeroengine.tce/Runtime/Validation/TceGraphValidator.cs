using System;
using System.Collections.Generic;

namespace ZeroEngine.TCE
{
    public static class TceGraphValidator
    {
        public static IReadOnlyList<TceValidationIssue> Validate(TceGraph graph)
        {
            var issues = new List<TceValidationIssue>();

            if (graph == null)
            {
                AddError(issues, TceValidationCodes.NullGraph, "graph", "Graph must not be null.");
                return issues;
            }

            ValidateRequiredList(graph.Triggers, "triggers", TceValidationCodes.MissingTrigger, issues);
            ValidateRequiredList(graph.Effects, "effects", TceValidationCodes.MissingEffect, issues);

            ValidateComponents(graph.Triggers, "triggers", typeof(ITceTrigger), issues);
            ValidateComponents(graph.Conditions, "conditions", typeof(ITceCondition), issues);
            ValidateComponents(graph.Effects, "effects", typeof(ITceEffect), issues);

            return issues;
        }

        private static void ValidateRequiredList<TData>(
            IReadOnlyList<TData> dataItems,
            string path,
            string missingCode,
            List<TceValidationIssue> issues)
            where TData : TceComponentData
        {
            if (dataItems == null || dataItems.Count == 0)
                AddError(issues, missingCode, path, $"{path} must contain at least one component.");
        }

        private static void ValidateComponents<TData>(
            IReadOnlyList<TData> dataItems,
            string path,
            Type expectedRuntimeType,
            List<TceValidationIssue> issues)
            where TData : TceComponentData
        {
            if (dataItems == null)
                return;

            for (int i = 0; i < dataItems.Count; i++)
            {
                string componentPath = $"{path}[{i}]";
                TData data = dataItems[i];

                if (data == null)
                {
                    AddError(issues, TceValidationCodes.NullComponent, componentPath, "Component data must not be null.");
                    continue;
                }

                Type runtimeType = data.RuntimeType;
                if (runtimeType == null)
                {
                    AddError(issues, TceValidationCodes.RuntimeTypeMissing, componentPath, "Component runtime type must not be null.");
                    continue;
                }

                if (!expectedRuntimeType.IsAssignableFrom(runtimeType))
                {
                    AddError(issues, TceValidationCodes.RuntimeTypeMismatch, componentPath, $"{runtimeType.FullName} must implement {expectedRuntimeType.Name}.");
                    continue;
                }

                if (data is ITceComponentDataValidator validator)
                    validator.Validate(new TceComponentValidationContext(data, componentPath), issues);
            }
        }

        private static void AddError(List<TceValidationIssue> issues, string code, string path, string message)
        {
            issues.Add(new TceValidationIssue(TceValidationSeverity.Error, code, path, message));
        }
    }
}
