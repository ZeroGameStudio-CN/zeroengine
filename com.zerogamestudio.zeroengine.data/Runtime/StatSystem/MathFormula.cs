using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.StatSystem.Formula
{
    [Serializable]
    public class MathContext
    {
        public IStatProvider Source;
        public IStatProvider Target;

        public MathContext(IStatProvider source, IStatProvider target)
        {
            Source = source;
            Target = target;
        }
    }

    public enum MathOperationType { Add, Subtract, Multiply, Divide }
    public enum ValueProviderType { Constant, InputValue, SourceStat, TargetStat }

    [CreateAssetMenu(menuName = "ZeroEngine/StatSystem/Math Formula")]
    public class MathFormula : ScriptableObject
    {
        public float InitialValue;
        public List<OperationStep> Steps = new List<OperationStep>();

        public float Evaluate(MathContext ctx = null, float? input = null)
        {
            float result = input ?? InitialValue;
            foreach (var step in Steps)
            {
                result = step.Apply(result, ctx, input);
            }
            return result;
        }
    }

    [Serializable]
    public class OperationStep
    {
        public MathOperationType Operation;
        public ValueProviderType ProviderType;
        public float ConstantValue;
        public StatId StatId;

        public float Apply(float current, MathContext ctx, float? input)
        {
            float val = GetValue(ctx, input);
            switch (Operation)
            {
                case MathOperationType.Add: return current + val;
                case MathOperationType.Subtract: return current - val;
                case MathOperationType.Multiply: return current * val;
                case MathOperationType.Divide: return val != 0 ? current / val : current;
            }
            return current;
        }

        private float GetValue(MathContext ctx, float? input)
        {
            switch (ProviderType)
            {
                case ValueProviderType.Constant: return ConstantValue;
                case ValueProviderType.InputValue: return input ?? 0;
                case ValueProviderType.SourceStat: return ctx?.Source?.GetStatValue(StatId) ?? 0;
                case ValueProviderType.TargetStat: return ctx?.Target?.GetStatValue(StatId) ?? 0;
            }
            return 0;
        }
    }
}
