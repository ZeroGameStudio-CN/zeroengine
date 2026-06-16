using System;
using System.Collections.Generic;

namespace ZeroEngine.TCE
{
    public enum TceComparison
    {
        GreaterThan = 0,
        LessThan = 1,
        GreaterThanOrEqualTo = 2,
        LessThanOrEqualTo = 3,
        EqualTo = 4
    }

    public interface ITceNumericValueSource
    {
        float Value { get; }
    }

    public readonly struct NumericValueSource : ITceNumericValueSource
    {
        public NumericValueSource(float value)
        {
            Value = value;
        }

        public float Value { get; }
    }

    public sealed class NumericSourceCondition : TceCondition<NumericSourceConditionData>
    {
        public override bool Check(ITceActor target, object source)
        {
            return source is ITceNumericValueSource valueSource && Compare(valueSource.Value, Data.RequiredValue, Data.Comparison);
        }

        private static bool Compare(float actual, float required, TceComparison comparison)
        {
            return comparison switch
            {
                TceComparison.GreaterThan => actual > required,
                TceComparison.LessThan => actual < required,
                TceComparison.GreaterThanOrEqualTo => actual >= required,
                TceComparison.LessThanOrEqualTo => actual <= required,
                TceComparison.EqualTo => Math.Abs(actual - required) <= 0.0001f,
                _ => false
            };
        }
    }

    [Serializable]
    [TceComponentDoc(TceComponentDocCategory.Condition, "zeroengine.tce.condition.numeric_source", "Numeric Source", "Compares a numeric value supplied by the trigger source.", "Use this condition when the trigger source can expose a simple numeric value without depending on a project-specific stat, resource, or damage model.")]
    public sealed class NumericSourceConditionData : TceConditionData<NumericSourceCondition>, ITceComponentDataValidator
    {
        [TceFieldDoc("Numeric threshold compared against the trigger source value.")]
        public float RequiredValue;

        [TceFieldDoc("Comparison operation applied to the trigger source value.")]
        public TceComparison Comparison = TceComparison.GreaterThanOrEqualTo;

        public void Validate(TceComponentValidationContext context, List<TceValidationIssue> issues)
        {
            if (!Enum.IsDefined(typeof(TceComparison), Comparison))
                issues.Add(new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.InvalidEnumValue, $"{context.Path}.Comparison", "Comparison must be a defined TceComparison value."));
        }
    }
}
