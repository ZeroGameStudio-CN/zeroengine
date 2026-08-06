using UnityEngine;

namespace ZeroEngine.Formula
{
    public enum FormulaRoundingMode
    {
        Floor = 0,
        Ceil = 1,
        Round = 2,
        Truncate = 3,
    }

    public static class FormulaValueUtility
    {
        public static int ToInt(float value, FormulaRoundingMode mode)
        {
            return mode switch
            {
                FormulaRoundingMode.Floor => Mathf.FloorToInt(value),
                FormulaRoundingMode.Ceil => Mathf.CeilToInt(value),
                FormulaRoundingMode.Round => Mathf.RoundToInt(value),
                FormulaRoundingMode.Truncate => (int)value,
                _ => Mathf.FloorToInt(value),
            };
        }

        public static int EvaluateToInt(
            IFormulaDefinition formula,
            IFormulaEvaluationContext context,
            FormulaProviderRegistry registry,
            FormulaRoundingMode mode,
            int fallback,
            out FormulaEvaluationReport report)
        {
            if (formula == null)
            {
                report = new FormulaEvaluationReport(null, "<null>");
                report.AddDiagnostic(FormulaDiagnosticSeverity.Error, FormulaDiagnosticCode.NullFormula, "Formula is null.");
                report.SetResult(fallback, false);
                return fallback;
            }

            return FormulaEvaluator.TryEvaluate(formula, context, registry, out var value, out report)
                ? ToInt(value, mode)
                : fallback;
        }

        public static int EvaluateToInt(
            IFormulaDefinition formula,
            IFormulaEvaluationContext context,
            FormulaProviderRegistry registry,
            IFormulaRandomSource randomSource,
            FormulaRoundingMode mode,
            int fallback,
            out FormulaEvaluationReport report)
        {
            if (formula == null)
            {
                report = new FormulaEvaluationReport(null, "<null>");
                report.AddDiagnostic(FormulaDiagnosticSeverity.Error, FormulaDiagnosticCode.NullFormula, "Formula is null.");
                report.SetResult(fallback, false);
                return fallback;
            }

            return FormulaEvaluator.TryEvaluate(formula, context, registry, randomSource, out var value, out report)
                ? ToInt(value, mode)
                : fallback;
        }
    }
}
