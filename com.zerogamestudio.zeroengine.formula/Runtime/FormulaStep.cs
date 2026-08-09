using System;
using UnityEngine;

namespace ZeroEngine.Formula
{
    public enum FormulaOperationType
    {
        Add = 0,
        Subtract = 1,
        Multiply = 2,
        Divide = 3,
        MultiplyFactor = 4,
    }

    [Serializable]
    public sealed class FormulaStep
    {
        public FormulaStep(FormulaOperationType operation, FormulaValueSource source)
        {
            Operation = operation;
            Source = source;
        }

        [field: SerializeField] public FormulaOperationType Operation { get; private set; }
        [field: SerializeField] public FormulaValueSource Source { get; private set; }

        public static FormulaStep Create(FormulaOperationType operation, FormulaValueSource source)
        {
            return new FormulaStep(operation, source);
        }
    }
}
