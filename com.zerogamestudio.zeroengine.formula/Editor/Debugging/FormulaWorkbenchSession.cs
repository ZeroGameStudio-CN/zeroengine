using System.Collections.Generic;

namespace ZeroEngine.Formula.Editor
{
    public sealed class FormulaWorkbenchSession
    {
        public const string CurrentPreviewCaseId = "current";

        private readonly List<FormulaPreviewCaseAsset> previewCaseAssets = new();
        private string curveInputKey = string.Empty;
        private float curveMin;
        private float curveMax = 10f;
        private int curveSampleCount = 11;

        public IReadOnlyList<FormulaPreviewCaseAsset> PreviewCaseAssets => previewCaseAssets;
        public string CurveInputKey => curveInputKey;
        public float CurveMin => curveMin;
        public float CurveMax => curveMax;
        public int CurveSampleCount => curveSampleCount;

        public void AddPreviewCaseAsset(FormulaPreviewCaseAsset asset)
        {
            if (asset != null && !previewCaseAssets.Contains(asset))
                previewCaseAssets.Add(asset);
        }

        public void AddPreviewCaseAssetSlot()
        {
            previewCaseAssets.Add(null);
        }

        public void SetPreviewCaseAssetAt(int index, FormulaPreviewCaseAsset asset)
        {
            if (index >= 0 && index < previewCaseAssets.Count)
                previewCaseAssets[index] = asset;
            else if (asset != null)
                AddPreviewCaseAsset(asset);
        }

        public void RemovePreviewCaseAssetAt(int index)
        {
            if (index >= 0 && index < previewCaseAssets.Count)
                previewCaseAssets.RemoveAt(index);
        }

        public void SetCurve(string inputKey, float min, float max, int sampleCount)
        {
            curveInputKey = inputKey ?? string.Empty;
            curveMin = min;
            curveMax = max;
            curveSampleCount = sampleCount;
        }

        public FormulaPreviewBatchReport EvaluateBatch(
            FormulaAsset formula,
            FormulaEditorProfile profile,
            FormulaPreviewValueSet currentValues)
        {
            var cases = new List<FormulaPreviewCase>
            {
                new FormulaPreviewCase(
                    CurrentPreviewCaseId,
                    "当前输入",
                    currentValues ?? new FormulaPreviewValueSet(null),
                    "Workbench 当前预览输入。"),
            };

            if (profile != null)
            {
                foreach (var previewCase in profile.DefaultPreviewCases)
                {
                    if (previewCase != null)
                        cases.Add(previewCase);
                }
            }

            foreach (var asset in previewCaseAssets)
            {
                if (asset != null)
                    cases.Add(asset.CreatePreviewCase());
            }

            return FormulaPreviewRunner.EvaluateCases(formula, profile, cases);
        }

        public FormulaCurvePreviewReport BuildCurve(
            FormulaAsset formula,
            FormulaEditorProfile profile,
            FormulaPreviewValueSet baseValues)
        {
            return FormulaCurvePreview.BuildCurve(
                formula,
                profile,
                baseValues,
                curveInputKey,
                curveMin,
                curveMax,
                curveSampleCount);
        }

        public string ExportBatchJson(FormulaPreviewBatchReport report)
        {
            return FormulaPreviewReportExporter.ToJson(report);
        }

        public string ExportBatchMarkdown(FormulaPreviewBatchReport report)
        {
            return FormulaPreviewReportExporter.ToMarkdown(report);
        }
    }
}
