using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Formula
{
    public static class FormulaEvaluator
    {
        public static bool TryEvaluate(
            IFormulaDefinition formula,
            IFormulaEvaluationContext context,
            FormulaProviderRegistry registry,
            out float value,
            out FormulaEvaluationReport report)
        {
            return TryEvaluate(formula, context, registry, out value, out report, null);
        }

        private static bool TryEvaluate(
            IFormulaDefinition formula,
            IFormulaEvaluationContext context,
            FormulaProviderRegistry registry,
            out float value,
            out FormulaEvaluationReport report,
            HashSet<IFormulaDefinition> visited)
        {
            value = 0f;
            report = new FormulaEvaluationReport(formula as UnityEngine.Object, formula?.FormulaName ?? "<null>");
            context ??= FormulaDictionaryEvaluationContext.Empty;
            registry ??= FormulaProviderRegistry.Empty;
            visited ??= new HashSet<IFormulaDefinition>();

            if (formula == null)
            {
                report.AddDiagnostic(FormulaDiagnosticSeverity.Error, FormulaDiagnosticCode.NullFormula, "Formula is null.");
                report.SetResult(0f, false);
                return false;
            }

            if (!visited.Add(formula))
            {
                report.AddDiagnostic(
                    FormulaDiagnosticSeverity.Error,
                    FormulaDiagnosticCode.CircularReference,
                    $"Formula circular reference: {formula.FormulaName}");
                report.SetResult(0f, false);
                return false;
            }

            try
            {
                var current = formula.InitialValue;
                var success = true;

                for (var i = 0; i < formula.StepCount; i++)
                {
                    if (!formula.TryGetStep(i, out var step) || step == null)
                    {
                        report.AddDiagnostic(
                            FormulaDiagnosticSeverity.Error,
                            FormulaDiagnosticCode.InvalidOperation,
                            $"Formula {formula.FormulaName} has a null step at index {i}.");
                        success = false;
                        continue;
                    }

                    var before = current;
                    if (!TryGetSourceValue(step.Source, context, registry, report, visited, out var stepValue, out var label))
                        success = false;

                    current = ApplyOperation(step.Operation, current, stepValue, report);
                    report.AddStep(new FormulaStepEvaluation(
                        i,
                        step.Operation,
                        step.Source?.SourceType ?? FormulaValueSourceType.Constant,
                        label,
                        before,
                        stepValue,
                        current));
                }

                if (float.IsNaN(current) || float.IsInfinity(current))
                {
                    report.AddDiagnostic(
                        FormulaDiagnosticSeverity.Error,
                        FormulaDiagnosticCode.NonFiniteResult,
                        $"Formula {formula.FormulaName} evaluated to a non-finite value: {current}.");
                    current = 0f;
                    success = false;
                }

                success = success && !report.HasErrors;
                if (!success)
                    current = 0f;

                value = current;
                report.SetResult(current, success);
                return success;
            }
            finally
            {
                visited.Remove(formula);
            }
        }

        private static bool TryGetSourceValue(
            FormulaValueSource source,
            IFormulaEvaluationContext context,
            FormulaProviderRegistry registry,
            FormulaEvaluationReport report,
            HashSet<IFormulaDefinition> visited,
            out float value,
            out string label)
        {
            value = 0f;
            label = "<null source>";
            if (source == null)
            {
                report.AddDiagnostic(FormulaDiagnosticSeverity.Error, FormulaDiagnosticCode.InvalidProvider, "Formula source is null.");
                return false;
            }

            switch (source.SourceType)
            {
                case FormulaValueSourceType.Constant:
                    value = source.ConstantValue;
                    label = source.ConstantValue.ToString("0.###");
                    return true;
                case FormulaValueSourceType.Provider:
                    label = source.ProviderId;
                    return TryGetProviderValue(source, context, registry, report, out value);
                case FormulaValueSourceType.NestedFormula:
                    label = source.NestedFormula ? source.NestedFormula.name : "<null nested formula>";
                    return TryGetNestedFormulaValue(source, context, registry, report, visited, out value);
                default:
                    report.AddDiagnostic(
                        FormulaDiagnosticSeverity.Error,
                        FormulaDiagnosticCode.InvalidProvider,
                        $"Unsupported source type: {source.SourceType}");
                    return false;
            }
        }

        private static bool TryGetProviderValue(
            FormulaValueSource source,
            IFormulaEvaluationContext context,
            FormulaProviderRegistry registry,
            FormulaEvaluationReport report,
            out float value)
        {
            value = 0f;
            if (!registry.TryGetProvider(source.ProviderId, out var provider))
            {
                report.AddDiagnostic(
                    FormulaDiagnosticSeverity.Error,
                    FormulaDiagnosticCode.InvalidProvider,
                    $"Formula provider is not registered: {source.ProviderId}");
                return false;
            }

            try
            {
                return provider.TryGetValue(
                    new FormulaProviderRequest(source.ProviderId, source.Parameters),
                    context,
                    out value,
                    new FormulaDiagnosticSink(report));
            }
            catch (System.Exception ex)
            {
                report.AddDiagnostic(
                    FormulaDiagnosticSeverity.Error,
                    FormulaDiagnosticCode.ProviderException,
                    $"Formula provider {source.ProviderId} failed: {ex.Message}");
                return false;
            }
        }

        private static bool TryGetNestedFormulaValue(
            FormulaValueSource source,
            IFormulaEvaluationContext context,
            FormulaProviderRegistry registry,
            FormulaEvaluationReport report,
            HashSet<IFormulaDefinition> visited,
            out float value)
        {
            value = 0f;
            if (!(source.NestedFormula is IFormulaDefinition nested))
            {
                report.AddDiagnostic(
                    FormulaDiagnosticSeverity.Error,
                    FormulaDiagnosticCode.NullFormula,
                    "Nested formula reference is null or does not implement IFormulaDefinition.");
                return false;
            }

            var success = TryEvaluate(nested, context, registry, out value, out var nestedReport, visited);
            report.AddChildReport(nestedReport);
            foreach (var diagnostic in nestedReport.Diagnostics)
            {
                report.AddDiagnostic(
                    diagnostic.Severity,
                    diagnostic.Code,
                    $"{nested.FormulaName}: {diagnostic.Message}");
            }

            return success;
        }

        private static float ApplyOperation(
            FormulaOperationType operation,
            float current,
            float value,
            FormulaEvaluationReport report)
        {
            switch (operation)
            {
                case FormulaOperationType.Add:
                    return current + value;
                case FormulaOperationType.Subtract:
                    return current - value;
                case FormulaOperationType.Multiply:
                    return current * value;
                case FormulaOperationType.Divide:
                    if (Mathf.Approximately(value, 0f))
                    {
                        report.AddDiagnostic(
                            FormulaDiagnosticSeverity.Warning,
                            FormulaDiagnosticCode.DivideByZero,
                            "Divide by zero: keeping the previous result for compatibility.");
                        return current;
                    }

                    return current / value;
                case FormulaOperationType.MultiplyFactor:
                    return current * (1f + value);
                default:
                    report.AddDiagnostic(
                        FormulaDiagnosticSeverity.Error,
                        FormulaDiagnosticCode.InvalidOperation,
                        $"Unsupported operation: {operation}");
                    return current;
            }
        }
    }
}
