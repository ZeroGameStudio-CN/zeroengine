using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Formula.Editor
{
    public enum FormulaCatalogStatus
    {
        Draft,
        Active,
        Deprecated,
    }

    [Serializable]
    public struct FormulaResultRange
    {
        [SerializeField] private bool enabled;
        [SerializeField] private float min;
        [SerializeField] private float max;

        public FormulaResultRange(bool enabled, float min, float max)
        {
            this.enabled = enabled;
            this.min = min;
            this.max = max;
        }

        public static FormulaResultRange None { get; } = new(false, 0f, 0f);

        public bool Enabled => enabled;
        public float Min => min;
        public float Max => max;
    }

    [Serializable]
    public sealed class FormulaCatalogEntry
    {
        [SerializeField] private FormulaAsset formula;
        [SerializeField] private string formulaGuid;
        [SerializeField] private string title;
        [SerializeField] private string purpose;
        [SerializeField] private string owner;
        [SerializeField] private string unit;
        [SerializeField] private List<string> tags = new();
        [SerializeField] private FormulaCatalogStatus status;
        [SerializeField] private FormulaResultRange expectedRange;
        [SerializeField] private string notes;

        public FormulaCatalogEntry(
            FormulaAsset formula,
            string formulaGuid,
            string title,
            string purpose,
            string owner,
            string unit,
            IEnumerable<string> tags,
            FormulaCatalogStatus status,
            FormulaResultRange expectedRange,
            string notes)
        {
            this.formula = formula;
            this.formulaGuid = formulaGuid ?? string.Empty;
            this.title = title ?? string.Empty;
            this.purpose = purpose ?? string.Empty;
            this.owner = owner ?? string.Empty;
            this.unit = unit ?? string.Empty;
            this.tags = tags == null ? new List<string>() : new List<string>(tags);
            this.status = status;
            this.expectedRange = expectedRange;
            this.notes = notes ?? string.Empty;
        }

        public FormulaAsset Formula => formula;
        public string FormulaGuid => formulaGuid ?? string.Empty;
        public string Title => title ?? string.Empty;
        public string Purpose => purpose ?? string.Empty;
        public string Owner => owner ?? string.Empty;
        public string Unit => unit ?? string.Empty;
        public IReadOnlyList<string> Tags => tags ?? (IReadOnlyList<string>)Array.Empty<string>();
        public FormulaCatalogStatus Status => status;
        public FormulaResultRange ExpectedRange => expectedRange;
        public string Notes => notes ?? string.Empty;
    }

    public sealed class FormulaCatalogLookup
    {
        private readonly List<FormulaCatalogEntry> entries;

        public FormulaCatalogLookup(IEnumerable<FormulaCatalogEntry> entries)
        {
            this.entries = entries == null
                ? new List<FormulaCatalogEntry>()
                : new List<FormulaCatalogEntry>(entries);
        }

        public bool TryGetEntry(
            FormulaAsset formula,
            string formulaGuid,
            out FormulaCatalogEntry entry)
        {
            foreach (var candidate in entries)
            {
                if (candidate == null)
                    continue;

                if (candidate.Formula != null && ReferenceEquals(candidate.Formula, formula))
                {
                    entry = candidate;
                    return true;
                }

                if (!string.IsNullOrEmpty(formulaGuid)
                    && string.Equals(candidate.FormulaGuid, formulaGuid, StringComparison.OrdinalIgnoreCase))
                {
                    entry = candidate;
                    return true;
                }
            }

            entry = null;
            return false;
        }
    }

    public sealed class FormulaCatalog : ScriptableObject
    {
        [SerializeField] private List<FormulaCatalogEntry> entries = new();

        public IReadOnlyList<FormulaCatalogEntry> Entries => entries;

        public FormulaCatalogLookup CreateLookup()
        {
            return new FormulaCatalogLookup(entries);
        }
    }
}
