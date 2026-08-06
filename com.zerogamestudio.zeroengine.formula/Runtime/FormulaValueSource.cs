using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Formula
{
    public enum FormulaValueSourceType
    {
        Constant = 0,
        Provider = 1,
        NestedFormula = 2,
        RandomInteger = 3,
    }

    [Serializable]
    public sealed class FormulaValueSource
    {
        [field: SerializeField] public FormulaValueSourceType SourceType { get; private set; }
        [field: SerializeField] public float ConstantValue { get; private set; }
        [field: SerializeField] public string ProviderId { get; private set; }
        [field: SerializeField] public List<FormulaParameter> Parameters { get; private set; } = new();
        [field: SerializeField] public UnityEngine.Object NestedFormula { get; private set; }
        [field: SerializeField] public int RandomMinInclusive { get; private set; }
        [field: SerializeField] public int RandomMaxInclusive { get; private set; }

        public static FormulaValueSource Constant(float value)
        {
            return new FormulaValueSource
            {
                SourceType = FormulaValueSourceType.Constant,
                ConstantValue = value,
            };
        }

        public static FormulaValueSource Provider(string providerId, params FormulaParameter[] parameters)
        {
            return new FormulaValueSource
            {
                SourceType = FormulaValueSourceType.Provider,
                ProviderId = providerId,
                Parameters = parameters == null ? new List<FormulaParameter>() : new List<FormulaParameter>(parameters),
            };
        }

        public static FormulaValueSource Nested(UnityEngine.Object nestedFormula)
        {
            return new FormulaValueSource
            {
                SourceType = FormulaValueSourceType.NestedFormula,
                NestedFormula = nestedFormula,
            };
        }

        public static FormulaValueSource RandomInteger(int minInclusive, int maxInclusive)
        {
            return new FormulaValueSource
            {
                SourceType = FormulaValueSourceType.RandomInteger,
                RandomMinInclusive = minInclusive,
                RandomMaxInclusive = maxInclusive,
            };
        }
    }
}
