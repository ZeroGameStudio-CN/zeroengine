using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZeroEngine.BuffSystem;
using ZeroEngine.StatSystem.Formula;

namespace ZeroEngine.Data.Editor
{
    public enum DataValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class DataValidationIssue
    {
        public DataValidationIssue(DataValidationSeverity severity, string assetName, string fieldPath, string message)
        {
            Severity = severity;
            AssetName = assetName;
            FieldPath = fieldPath;
            Message = message;
        }

        public DataValidationSeverity Severity { get; }
        public string AssetName { get; }
        public string FieldPath { get; }
        public string Message { get; }

        public override string ToString()
        {
            return $"[{Severity}] {AssetName}.{FieldPath}: {Message}";
        }
    }

    public static class DataConfigValidator
    {
        public static IReadOnlyList<DataValidationIssue> Validate(
            IEnumerable<BuffData> buffs = null,
            IEnumerable<MathFormula> formulas = null)
        {
            bool loadAll = buffs == null && formulas == null;
            var buffList = Resolve(buffs, loadAll);
            var formulaList = Resolve(formulas, loadAll);
            var issues = new List<DataValidationIssue>();

            AddDuplicateStringIssues(issues, buffList, buff => buff.BuffId, "BuffId", "Buff ID");

            foreach (var buff in buffList)
                ValidateBuff(issues, buff);
            foreach (var formula in formulaList)
                ValidateFormula(issues, formula);

            return issues;
        }

        public static IReadOnlyList<T> LoadAssets<T>() where T : UnityEngine.Object
        {
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(asset => asset != null)
                .ToList();
        }

        private static List<T> Resolve<T>(IEnumerable<T> source, bool loadAll) where T : UnityEngine.Object
        {
            if (source != null)
                return source.ToList();

            return loadAll ? LoadAssets<T>().ToList() : new List<T>();
        }

        private static void ValidateBuff(List<DataValidationIssue> issues, BuffData buff)
        {
            if (buff == null)
                return;

            if (string.IsNullOrWhiteSpace(buff.BuffId))
                Add(issues, DataValidationSeverity.Error, buff, "BuffId", "Buff must have a stable ID.");
            if (buff.Duration < 0f)
                Add(issues, DataValidationSeverity.Error, buff, "Duration", "Buff duration cannot be negative.");
            if (buff.MaxStacks <= 0)
                Add(issues, DataValidationSeverity.Error, buff, "MaxStacks", "Buff max stacks must be positive.");
            if (buff.TickInterval <= 0f)
                Add(issues, DataValidationSeverity.Error, buff, "TickInterval", "Buff tick interval must be positive.");

            var modifiers = buff.StatModifiers ?? new List<BuffStatModifierConfig>();
            for (int i = 0; i < modifiers.Count; i++)
            {
                var modifier = modifiers[i];
                if (modifier == null)
                {
                    Add(issues, DataValidationSeverity.Error, buff, $"StatModifiers[{i}]", "Buff stat modifier is empty.");
                    continue;
                }

                if (Mathf.Approximately(modifier.Value, 0f))
                    Add(issues, DataValidationSeverity.Warning, buff, $"StatModifiers[{i}].Value", "Buff stat modifier has a zero value.");
            }
        }

        private static void ValidateFormula(List<DataValidationIssue> issues, MathFormula formula)
        {
            if (formula == null)
                return;

            if (float.IsNaN(formula.InitialValue) || float.IsInfinity(formula.InitialValue))
                Add(issues, DataValidationSeverity.Error, formula, "InitialValue", "Formula initial value must be finite.");

            var steps = formula.Steps ?? new List<OperationStep>();
            if (steps.Count == 0)
                Add(issues, DataValidationSeverity.Warning, formula, "Steps", "Math formula has no operation steps.");

            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                if (step == null)
                {
                    Add(issues, DataValidationSeverity.Error, formula, $"Steps[{i}]", "Formula operation step is empty.");
                    continue;
                }

                if (step.Operation == MathOperationType.Divide
                    && step.ProviderType == ValueProviderType.Constant
                    && Mathf.Approximately(step.ConstantValue, 0f))
                {
                    Add(issues, DataValidationSeverity.Error, formula, $"Steps[{i}].ConstantValue", "Formula cannot divide by a constant zero.");
                }
            }
        }

        private static void AddDuplicateStringIssues<T>(
            List<DataValidationIssue> issues,
            IEnumerable<T> assets,
            System.Func<T, string> getId,
            string fieldPath,
            string label)
            where T : UnityEngine.Object
        {
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var asset in assets.Where(asset => asset != null))
            {
                string id = getId(asset)?.Trim();
                if (string.IsNullOrEmpty(id))
                    continue;

                if (!seen.Add(id))
                    Add(issues, DataValidationSeverity.Error, asset, fieldPath, $"{label} '{id}' is duplicated.");
            }
        }

        private static void Add(List<DataValidationIssue> issues, DataValidationSeverity severity, UnityEngine.Object asset, string fieldPath, string message)
        {
            issues.Add(new DataValidationIssue(severity, string.IsNullOrEmpty(asset.name) ? asset.GetType().Name : asset.name, fieldPath, message));
        }
    }
}
