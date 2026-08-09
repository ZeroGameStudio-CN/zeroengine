using System.Collections.Generic;

namespace ZeroEngine.Formula
{
    public sealed class FormulaDictionaryEvaluationContext : IFormulaEvaluationContext
    {
        private readonly Dictionary<string, float> values = new();
        private readonly Dictionary<string, object> objects = new();

        public static FormulaDictionaryEvaluationContext Empty => new();

        public void SetValue(string key, float value) => values[key] = value;
        public void SetObject(string key, object value) => objects[key] = value;

        public bool TryGetValue(string key, out float value)
        {
            return values.TryGetValue(key, out value);
        }

        public bool TryGetObject<T>(string key, out T value) where T : class
        {
            if (objects.TryGetValue(key, out var obj) && obj is T typed)
            {
                value = typed;
                return true;
            }

            value = null;
            return false;
        }
    }
}
