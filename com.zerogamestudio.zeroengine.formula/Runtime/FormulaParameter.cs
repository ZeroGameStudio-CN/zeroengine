using System;
using UnityEngine;

namespace ZeroEngine.Formula
{
    public enum FormulaParameterType
    {
        String = 0,
        Int = 1,
        Float = 2,
        Bool = 3,
        Object = 4,
    }

    [Serializable]
    public sealed class FormulaParameter
    {
        [field: SerializeField] public string Name { get; private set; }
        [field: SerializeField] public FormulaParameterType Type { get; private set; }
        [field: SerializeField] public string StringValue { get; private set; }
        [field: SerializeField] public int IntValue { get; private set; }
        [field: SerializeField] public float FloatValue { get; private set; }
        [field: SerializeField] public bool BoolValue { get; private set; }
        [field: SerializeField] public UnityEngine.Object ObjectValue { get; private set; }

        public static FormulaParameter String(string name, string value)
        {
            return new FormulaParameter { Name = name, Type = FormulaParameterType.String, StringValue = value };
        }

        public static FormulaParameter Int(string name, int value)
        {
            return new FormulaParameter { Name = name, Type = FormulaParameterType.Int, IntValue = value };
        }

        public static FormulaParameter Float(string name, float value)
        {
            return new FormulaParameter { Name = name, Type = FormulaParameterType.Float, FloatValue = value };
        }

        public static FormulaParameter Bool(string name, bool value)
        {
            return new FormulaParameter { Name = name, Type = FormulaParameterType.Bool, BoolValue = value };
        }

        public static FormulaParameter Object(string name, UnityEngine.Object value)
        {
            return new FormulaParameter { Name = name, Type = FormulaParameterType.Object, ObjectValue = value };
        }
    }
}
