using System.Collections.Generic;

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
                out value,
                out report);
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
            if (profile == null)
                return context;

            foreach (var input in profile.PreviewInputs)
            {
                var value = overrides != null && overrides.TryGetValue(input.Key, out var overrideValue)
                    ? overrideValue
                    : input.DefaultValue;
                context.SetValue(input.Key, value);
            }

            return context;
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
    }
}
