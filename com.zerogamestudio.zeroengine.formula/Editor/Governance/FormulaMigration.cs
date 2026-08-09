using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace ZeroEngine.Formula.Editor
{
    public enum FormulaMigrationKind
    {
        ProviderIdRename,
        ParameterKeyRename,
    }

    public sealed class FormulaMigrationChange
    {
        public FormulaMigrationChange(int stepIndex, string oldValue, string newValue, string message)
        {
            StepIndex = stepIndex;
            OldValue = oldValue ?? string.Empty;
            NewValue = newValue ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public int StepIndex { get; }
        public string OldValue { get; }
        public string NewValue { get; }
        public string Message { get; }
    }

    public sealed class FormulaMigrationReport
    {
        public FormulaMigrationReport(
            FormulaMigrationKind kind,
            bool applied,
            IReadOnlyList<FormulaMigrationChange> changes)
        {
            Kind = kind;
            Applied = applied;
            Changes = changes ?? Array.Empty<FormulaMigrationChange>();
        }

        public FormulaMigrationKind Kind { get; }
        public bool Applied { get; }
        public IReadOnlyList<FormulaMigrationChange> Changes { get; }
        public bool HasChanges => Changes.Count > 0;
    }

    public static class FormulaMigration
    {
        private static readonly FieldInfo ProviderIdField =
            typeof(FormulaValueSource).GetField("<ProviderId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo ParameterNameField =
            typeof(FormulaParameter).GetField("<Name>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

        public static FormulaMigrationReport ProviderIdRename(
            FormulaAsset formula,
            string oldProviderId,
            string newProviderId,
            bool apply)
        {
            var changes = new List<FormulaMigrationChange>();
            if (formula == null || string.IsNullOrEmpty(oldProviderId))
                return new FormulaMigrationReport(FormulaMigrationKind.ProviderIdRename, apply, changes.AsReadOnly());

            for (var index = 0; index < formula.StepCount; index++)
            {
                if (!formula.TryGetStep(index, out var step)
                    || step?.Source?.SourceType != FormulaValueSourceType.Provider
                    || !string.Equals(step.Source.ProviderId, oldProviderId, StringComparison.Ordinal))
                    continue;

                changes.Add(new FormulaMigrationChange(
                    index,
                    oldProviderId,
                    newProviderId,
                    $"Step {index}: provider id '{oldProviderId}' -> '{newProviderId}'."));

                if (apply)
                    ProviderIdField?.SetValue(step.Source, newProviderId ?? string.Empty);
            }

            MarkDirtyIfApplied(formula, apply, changes);
            return new FormulaMigrationReport(FormulaMigrationKind.ProviderIdRename, apply, changes.AsReadOnly());
        }

        public static FormulaMigrationReport ParameterKeyRename(
            FormulaAsset formula,
            string providerId,
            string oldParameterKey,
            string newParameterKey,
            bool apply)
        {
            var changes = new List<FormulaMigrationChange>();
            if (formula == null || string.IsNullOrEmpty(oldParameterKey))
                return new FormulaMigrationReport(FormulaMigrationKind.ParameterKeyRename, apply, changes.AsReadOnly());

            for (var stepIndex = 0; stepIndex < formula.StepCount; stepIndex++)
            {
                if (!formula.TryGetStep(stepIndex, out var step)
                    || step?.Source?.SourceType != FormulaValueSourceType.Provider
                    || (!string.IsNullOrEmpty(providerId)
                        && !string.Equals(step.Source.ProviderId, providerId, StringComparison.Ordinal)))
                    continue;

                foreach (var parameter in step.Source.Parameters)
                {
                    if (parameter == null
                        || !string.Equals(parameter.Name, oldParameterKey, StringComparison.Ordinal))
                        continue;

                    changes.Add(new FormulaMigrationChange(
                        stepIndex,
                        oldParameterKey,
                        newParameterKey,
                        $"Step {stepIndex}: parameter key '{oldParameterKey}' -> '{newParameterKey}'."));

                    if (apply)
                        ParameterNameField?.SetValue(parameter, newParameterKey ?? string.Empty);
                }
            }

            MarkDirtyIfApplied(formula, apply, changes);
            return new FormulaMigrationReport(FormulaMigrationKind.ParameterKeyRename, apply, changes.AsReadOnly());
        }

        private static void MarkDirtyIfApplied(
            FormulaAsset formula,
            bool apply,
            IReadOnlyList<FormulaMigrationChange> changes)
        {
            if (apply && formula != null && changes.Count > 0)
                EditorUtility.SetDirty(formula);
        }
    }
}
