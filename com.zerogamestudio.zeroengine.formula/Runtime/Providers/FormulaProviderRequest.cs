using System.Collections.Generic;
using System.Linq;

namespace ZeroEngine.Formula
{
    public sealed class FormulaProviderRequest
    {
        private readonly IReadOnlyList<FormulaParameter> parameters;

        public FormulaProviderRequest(string providerId, IReadOnlyList<FormulaParameter> parameters)
        {
            ProviderId = providerId;
            this.parameters = parameters ?? System.Array.Empty<FormulaParameter>();
        }

        public string ProviderId { get; }
        public IReadOnlyList<FormulaParameter> Parameters => parameters;

        public bool TryGetInt(string name, out int value)
        {
            var parameter = parameters.FirstOrDefault(p => p.Name == name && p.Type == FormulaParameterType.Int);
            value = parameter?.IntValue ?? 0;
            return parameter != null;
        }

        public bool TryGetString(string name, out string value)
        {
            var parameter = parameters.FirstOrDefault(p => p.Name == name && p.Type == FormulaParameterType.String);
            value = parameter?.StringValue;
            return parameter != null;
        }

        public bool TryGetFloat(string name, out float value)
        {
            var parameter = parameters.FirstOrDefault(p => p.Name == name && p.Type == FormulaParameterType.Float);
            value = parameter?.FloatValue ?? 0f;
            return parameter != null;
        }

        public bool TryGetBool(string name, out bool value)
        {
            var parameter = parameters.FirstOrDefault(p => p.Name == name && p.Type == FormulaParameterType.Bool);
            value = parameter?.BoolValue ?? false;
            return parameter != null;
        }

        public bool TryGetObject(string name, out UnityEngine.Object value)
        {
            var parameter = parameters.FirstOrDefault(p => p.Name == name && p.Type == FormulaParameterType.Object);
            value = parameter?.ObjectValue;
            return parameter != null;
        }
    }
}
