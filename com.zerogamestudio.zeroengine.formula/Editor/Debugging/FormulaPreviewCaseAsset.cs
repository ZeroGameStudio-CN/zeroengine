using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Formula.Editor
{
    [CreateAssetMenu(
        fileName = "Formula Preview Case",
        menuName = "ZeroEngine/Formula/Preview Case")]
    public sealed class FormulaPreviewCaseAsset : ScriptableObject
    {
        [SerializeField] private string caseId;
        [SerializeField] private string displayName;
        [SerializeField] private string description;
        [SerializeField] private List<FormulaPreviewValueRow> values = new();

        public string CaseId => caseId ?? string.Empty;
        public string DisplayName => displayName ?? string.Empty;
        public string Description => description ?? string.Empty;

        public FormulaPreviewCase CreatePreviewCase()
        {
            var previewValues = new List<FormulaPreviewValue>();
            foreach (var row in values)
            {
                if (row != null && !string.IsNullOrEmpty(row.Key))
                    previewValues.Add(new FormulaPreviewValue(row.Key, row.Value));
            }

            return new FormulaPreviewCase(
                CaseId,
                DisplayName,
                new FormulaPreviewValueSet(previewValues),
                Description);
        }

        public void Initialize(
            string id,
            string name,
            string caseDescription,
            IEnumerable<FormulaPreviewValue> previewValues)
        {
            caseId = id ?? string.Empty;
            displayName = name ?? string.Empty;
            description = caseDescription ?? string.Empty;
            values = new List<FormulaPreviewValueRow>();
            if (previewValues == null)
                return;

            foreach (var value in previewValues)
            {
                if (value != null)
                    values.Add(new FormulaPreviewValueRow(value.Key, value.Value));
            }
        }

        [Serializable]
        private sealed class FormulaPreviewValueRow
        {
            [SerializeField] private string key;
            [SerializeField] private float value;

            public FormulaPreviewValueRow()
            {
                key = string.Empty;
            }

            public FormulaPreviewValueRow(string key, float value)
            {
                this.key = key ?? string.Empty;
                this.value = value;
            }

            public string Key => key ?? string.Empty;
            public float Value => value;
        }
    }
}
