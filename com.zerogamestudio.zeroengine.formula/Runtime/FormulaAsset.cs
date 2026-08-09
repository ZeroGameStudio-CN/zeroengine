using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Formula
{
    [CreateAssetMenu(menuName = "ZeroEngine/Formula/Formula Asset")]
    public sealed class FormulaAsset : ScriptableObject, IFormulaDefinition
    {
        [SerializeField] private float initialValue;
        [SerializeField] private List<FormulaStep> steps = new();

        public string FormulaName => string.IsNullOrEmpty(name) ? "<formula>" : name;
        public float InitialValue => initialValue;
        public int StepCount => steps?.Count ?? 0;

        public bool TryGetStep(int index, out FormulaStep step)
        {
            if (steps == null || index < 0 || index >= steps.Count)
            {
                step = null;
                return false;
            }

            step = steps[index];
            return true;
        }
    }
}
