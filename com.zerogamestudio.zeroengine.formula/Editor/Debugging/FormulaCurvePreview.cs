using System;
using System.Collections.Generic;

namespace ZeroEngine.Formula.Editor
{
    public sealed class FormulaCurvePreviewPoint
    {
        public FormulaCurvePreviewPoint(
            float input,
            float result,
            bool succeeded,
            FormulaEvaluationReport report)
        {
            Input = input;
            Result = result;
            Succeeded = succeeded;
            Report = report;
        }

        public float Input { get; }
        public float Result { get; }
        public bool Succeeded { get; }
        public FormulaEvaluationReport Report { get; }
    }

    public sealed class FormulaCurvePreviewReport
    {
        public FormulaCurvePreviewReport(
            string inputKey,
            float min,
            float max,
            IReadOnlyList<FormulaCurvePreviewPoint> points)
        {
            InputKey = inputKey ?? string.Empty;
            Min = min;
            Max = max;
            Points = points ?? Array.Empty<FormulaCurvePreviewPoint>();
        }

        public string InputKey { get; }
        public float Min { get; }
        public float Max { get; }
        public IReadOnlyList<FormulaCurvePreviewPoint> Points { get; }
        public bool Succeeded
        {
            get
            {
                if (Points.Count == 0)
                    return false;

                foreach (var point in Points)
                {
                    if (point == null || !point.Succeeded)
                        return false;
                }

                return true;
            }
        }
    }

    public static class FormulaCurvePreview
    {
        public static FormulaCurvePreviewReport BuildCurve(
            FormulaAsset formula,
            FormulaEditorProfile profile,
            FormulaPreviewValueSet baseValues,
            string inputKey,
            float min,
            float max,
            int sampleCount)
        {
            var count = Math.Max(2, sampleCount);
            var points = new List<FormulaCurvePreviewPoint>(count);
            var values = baseValues == null
                ? new Dictionary<string, float>()
                : new Dictionary<string, float>(baseValues.ToDictionary());

            for (var index = 0; index < count; index++)
            {
                var input = count == 1
                    ? min
                    : min + ((max - min) * index / (count - 1));
                values[inputKey ?? string.Empty] = input;
                var context = FormulaEditorPreview.CreateContext(profile, values);
                var succeeded = FormulaEditorPreview.TryEvaluate(
                    formula,
                    profile,
                    context,
                    out var result,
                    out var report);
                points.Add(new FormulaCurvePreviewPoint(input, result, succeeded, report));
            }

            return new FormulaCurvePreviewReport(inputKey, min, max, points.AsReadOnly());
        }
    }
}
