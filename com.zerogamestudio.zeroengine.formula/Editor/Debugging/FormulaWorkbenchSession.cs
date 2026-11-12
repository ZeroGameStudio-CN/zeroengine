using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Formula.Editor
{
    [Serializable]
    public sealed class FormulaPreviewScenario
    {
        [SerializeField]
        private string id = string.Empty;
        [SerializeField]
        private string displayName = string.Empty;
        [SerializeField]
        private List<FormulaPreviewScenarioValue> values = new();

        public FormulaPreviewScenario(
            string id,
            string displayName,
            IEnumerable<FormulaPreviewValue> values)
        {
            this.id = id ?? string.Empty;
            this.displayName = displayName ?? string.Empty;
            if (values == null)
                return;

            foreach (var value in values)
            {
                if (value != null && !string.IsNullOrEmpty(value.Key))
                    this.values.Add(new FormulaPreviewScenarioValue(value.Key, value.Value));
            }
        }

        public string Id => id ?? string.Empty;
        public string DisplayName => displayName ?? string.Empty;

        public FormulaPreviewCase CreatePreviewCase()
        {
            return new FormulaPreviewCase(
                Id,
                DisplayName,
                CreateValueSet(),
                "工作台本地保存的预览场景。");
        }

        public FormulaPreviewValueSet CreateValueSet()
        {
            var previewValues = new List<FormulaPreviewValue>();
            if (values != null)
            {
                foreach (var value in values)
                {
                    if (value != null && !string.IsNullOrEmpty(value.Key))
                        previewValues.Add(new FormulaPreviewValue(value.Key, value.Value));
                }
            }

            return new FormulaPreviewValueSet(previewValues);
        }
    }

    [Serializable]
    public sealed class FormulaPreviewScenarioValue
    {
        [SerializeField]
        private string key = string.Empty;
        [SerializeField]
        private float value;

        public FormulaPreviewScenarioValue(string key, float value)
        {
            this.key = key ?? string.Empty;
            this.value = value;
        }

        public string Key => key ?? string.Empty;
        public float Value => value;
    }

    public static class FormulaPreviewScenarioStore
    {
        private const string EditorPrefsPrefix = "ZeroEngine.Formula.PreviewScenarios.";

        [Serializable]
        private sealed class ScenarioCollection
        {
            public List<FormulaPreviewScenario> items = new();
        }

        public static IReadOnlyList<FormulaPreviewScenario> Load(string profileId)
        {
            return Deserialize(EditorPrefs.GetString(GetStorageKey(profileId), string.Empty));
        }

        public static void Save(string profileId, IEnumerable<FormulaPreviewScenario> scenarios)
        {
            EditorPrefs.SetString(GetStorageKey(profileId), Serialize(scenarios));
        }

        public static string Serialize(IEnumerable<FormulaPreviewScenario> scenarios)
        {
            var collection = new ScenarioCollection();
            if (scenarios != null)
            {
                foreach (var scenario in scenarios)
                {
                    if (scenario != null)
                        collection.items.Add(scenario);
                }
            }

            return JsonUtility.ToJson(collection);
        }

        public static List<FormulaPreviewScenario> Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json))
                return new List<FormulaPreviewScenario>();

            try
            {
                var collection = JsonUtility.FromJson<ScenarioCollection>(json);
                return collection?.items ?? new List<FormulaPreviewScenario>();
            }
            catch (ArgumentException)
            {
                return new List<FormulaPreviewScenario>();
            }
        }

        private static string GetStorageKey(string profileId)
        {
            var scope = Application.dataPath + "|" + (profileId ?? string.Empty);
            return EditorPrefsPrefix + Hash128.Compute(scope);
        }
    }

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
            return EvaluateBatch(formula, profile, currentValues, null);
        }

        public FormulaPreviewBatchReport EvaluateBatch(
            FormulaAsset formula,
            FormulaEditorProfile profile,
            FormulaPreviewValueSet currentValues,
            IEnumerable<FormulaPreviewCase> additionalCases)
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

            if (additionalCases != null)
            {
                foreach (var previewCase in additionalCases)
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
