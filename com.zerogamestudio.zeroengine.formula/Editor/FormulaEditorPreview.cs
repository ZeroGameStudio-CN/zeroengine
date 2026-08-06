using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ZeroEngine.Formula.Editor
{
    public sealed class FormulaEditorPreviewState
    {
        private readonly Dictionary<string, float> values = new();

        public float GetValue(FormulaPreviewInputDescriptor descriptor)
        {
            if (descriptor == null)
                return 0f;

            if (!values.TryGetValue(descriptor.Key, out var value))
            {
                value = descriptor.DefaultValue;
                values[descriptor.Key] = value;
            }

            return value;
        }

        public void SetValue(string key, float value)
        {
            if (!string.IsNullOrEmpty(key))
                values[key] = value;
        }

        public void ResetToDefaults(FormulaEditorProfile profile)
        {
            values.Clear();
            if (profile == null)
                return;

            foreach (var input in profile.PreviewInputs)
                values[input.Key] = input.DefaultValue;
        }

        public FormulaDictionaryEvaluationContext CreateContext(FormulaEditorProfile profile)
        {
            return FormulaEditorPreview.CreateContext(profile, values);
        }

        public FormulaPreviewValueSet ToValueSet(FormulaEditorProfile profile)
        {
            var previewValues = new List<FormulaPreviewValue>();
            if (profile == null)
                return new FormulaPreviewValueSet(previewValues);

            foreach (var input in profile.PreviewInputs)
                previewValues.Add(new FormulaPreviewValue(input.Key, GetValue(input)));

            return new FormulaPreviewValueSet(previewValues);
        }
    }

    public static class FormulaEditorPreview
    {
        public const int RandomSeed = 0x5EED;

        public static bool TryEvaluate(
            FormulaAsset formula,
            FormulaEditorProfile profile,
            IFormulaEvaluationContext context,
            out float value,
            out FormulaEvaluationReport report)
        {
            return FormulaEvaluator.TryEvaluate(
                formula,
                context ?? CreateContext(profile),
                CreateRegistry(profile),
                CreateRandomSource(),
                out value,
                out report);
        }

        public static IFormulaRandomSource CreateRandomSource()
        {
            return new SystemFormulaRandomSource(new Random(RandomSeed));
        }

        public static FormulaDictionaryEvaluationContext CreateContext(FormulaEditorProfile profile)
        {
            return CreateContext(profile, null);
        }

        public static FormulaDictionaryEvaluationContext CreateContext(
            FormulaEditorProfile profile,
            IReadOnlyDictionary<string, float> overrides)
        {
            var context = FormulaDictionaryEvaluationContext.Empty;
            if (profile != null)
            {
                foreach (var input in profile.PreviewInputs)
                {
                    var value = overrides != null && overrides.TryGetValue(input.Key, out var overrideValue)
                        ? overrideValue
                        : input.DefaultValue;
                    context.SetValue(input.Key, value);
                }
            }

            if (overrides != null)
            {
                foreach (var entry in overrides)
                    context.SetValue(entry.Key, entry.Value);
            }

            return context;
        }

        public static string GetProviderPreviewInputKey(
            string providerId,
            IReadOnlyList<FormulaParameter> parameters)
        {
            var builder = new StringBuilder();
            builder.Append("provider:");
            builder.Append(providerId ?? string.Empty);

            if (parameters == null || parameters.Count == 0)
                return builder.ToString();

            var sortedParameters = new List<FormulaParameter>();
            foreach (var parameter in parameters)
            {
                if (parameter != null)
                    sortedParameters.Add(parameter);
            }

            sortedParameters.Sort(CompareParameters);
            foreach (var parameter in sortedParameters)
            {
                builder.Append('|');
                AppendEscaped(builder, parameter.Name);
                builder.Append('=');
                builder.Append(GetParameterValueToken(parameter));
            }

            return builder.ToString();
        }

        public static FormulaProviderRegistry CreateRegistry(FormulaEditorProfile profile)
        {
            if (profile == null || profile.Providers.Count == 0)
                return FormulaProviderRegistry.Empty;

            var registry = new FormulaProviderRegistry();
            foreach (var provider in profile.Providers)
                registry.Register(new ProfilePreviewFormulaProvider(provider));

            return registry;
        }

        private sealed class ProfilePreviewFormulaProvider : IFormulaValueProvider
        {
            private readonly FormulaProviderDescriptor descriptor;

            public ProfilePreviewFormulaProvider(FormulaProviderDescriptor descriptor)
            {
                this.descriptor = descriptor;
            }

            public string Id => descriptor.Id;

            public bool TryGetValue(
                FormulaProviderRequest request,
                IFormulaEvaluationContext context,
                out float value,
                FormulaDiagnosticSink diagnostics)
            {
                foreach (var parameter in descriptor.Parameters)
                {
                    if (!parameter.Required || HasParameter(request, parameter))
                        continue;

                    value = 0f;
                    diagnostics.Add(
                        FormulaDiagnosticSeverity.Error,
                        FormulaDiagnosticCode.InvalidParameter,
                        $"{descriptor.DisplayName} 缺少参数：{parameter.DisplayName} ({parameter.Key})");
                    return false;
                }

                var scopedInputKey = GetProviderPreviewInputKey(request.ProviderId, request.Parameters);
                if (context != null
                    && context.TryGetValue(scopedInputKey, out value))
                    return true;

                if (!string.IsNullOrEmpty(descriptor.PreviewInputKey)
                    && context != null
                    && context.TryGetValue(descriptor.PreviewInputKey, out value))
                    return true;

                value = descriptor.PreviewValue;
                return true;
            }

            private static bool HasParameter(
                FormulaProviderRequest request,
                FormulaParameterDescriptor parameter)
            {
                switch (parameter.Kind)
                {
                    case FormulaEditorParameterKind.String:
                        return request.TryGetString(parameter.Key, out _);
                    case FormulaEditorParameterKind.Int:
                    case FormulaEditorParameterKind.Enum:
                        return request.TryGetInt(parameter.Key, out _);
                    case FormulaEditorParameterKind.Float:
                        return request.TryGetFloat(parameter.Key, out _);
                    case FormulaEditorParameterKind.Bool:
                        return request.TryGetBool(parameter.Key, out _);
                    case FormulaEditorParameterKind.Object:
                        return request.TryGetObject(parameter.Key, out _);
                    default:
                        return false;
                }
            }
        }

        private static int CompareParameters(FormulaParameter left, FormulaParameter right)
        {
            var nameComparison = string.Compare(left?.Name, right?.Name, StringComparison.Ordinal);
            if (nameComparison != 0)
                return nameComparison;

            var leftType = left?.Type ?? FormulaParameterType.String;
            var rightType = right?.Type ?? FormulaParameterType.String;
            return leftType.CompareTo(rightType);
        }

        private static string GetParameterValueToken(FormulaParameter parameter)
        {
            switch (parameter.Type)
            {
                case FormulaParameterType.Int:
                    return "i:" + parameter.IntValue.ToString(CultureInfo.InvariantCulture);
                case FormulaParameterType.Float:
                    return "f:" + parameter.FloatValue.ToString("R", CultureInfo.InvariantCulture);
                case FormulaParameterType.Bool:
                    return "b:" + (parameter.BoolValue ? "true" : "false");
                case FormulaParameterType.Object:
                    return "o:" + (parameter.ObjectValue ? parameter.ObjectValue.GetInstanceID().ToString(CultureInfo.InvariantCulture) : "0");
                case FormulaParameterType.String:
                default:
                    var builder = new StringBuilder("s:");
                    AppendEscaped(builder, parameter.StringValue);
                    return builder.ToString();
            }
        }

        private static void AppendEscaped(StringBuilder builder, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            foreach (var character in value)
            {
                if (character == '\\' || character == '|' || character == '=')
                    builder.Append('\\');

                builder.Append(character);
            }
        }
    }
}
