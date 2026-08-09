using System.Collections.Generic;

namespace ZeroEngine.Formula
{
    public interface IFormulaDefinition
    {
        string FormulaName { get; }
        float InitialValue { get; }
        int StepCount { get; }
        bool TryGetStep(int index, out FormulaStep step);
    }

    public interface IFormulaEvaluationContext
    {
        bool TryGetValue(string key, out float value);
        bool TryGetObject<T>(string key, out T value) where T : class;
    }

    public sealed class FormulaRuntimeDefinition : IFormulaDefinition
    {
        private readonly IReadOnlyList<FormulaStep> steps;

        public FormulaRuntimeDefinition(string formulaName, float initialValue, IReadOnlyList<FormulaStep> steps)
        {
            FormulaName = string.IsNullOrEmpty(formulaName) ? "<formula>" : formulaName;
            InitialValue = initialValue;
            this.steps = steps ?? System.Array.Empty<FormulaStep>();
        }

        public string FormulaName { get; }
        public float InitialValue { get; }
        public int StepCount => steps.Count;

        public bool TryGetStep(int index, out FormulaStep step)
        {
            if (index < 0 || index >= steps.Count)
            {
                step = null;
                return false;
            }

            step = steps[index];
            return true;
        }
    }
}
