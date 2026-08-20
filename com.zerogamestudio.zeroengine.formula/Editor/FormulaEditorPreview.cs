using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ZeroEngine.Formula.Editor
{
    public sealed class FormulaPreviewFieldDescriptor
    {
        public FormulaPreviewFieldDescriptor(
            string key,
            string displayName,
            string category,
            string description,
            FormulaPreviewInputKind kind,
            float defaultValue)
        {
            Key = key ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Category = string.IsNullOrWhiteSpace(category) ? FormulaEditorLabels.GeneralCategory : category;
            Description = description ?? string.Empty;
            Kind = kind;
            DefaultValue = defaultValue;
        }

        public string Key { get; }
        public string DisplayName { get; }
        public string Category { get; }
        public string Description { get; }
        public FormulaPreviewInputKind Kind { get; }
        public float DefaultValue { get; }
    }

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

        public float GetValue(FormulaPreviewFieldDescriptor descriptor)
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

        public void ResetToDefaults(
            FormulaEditorProfile profile,
            IReadOnlyList<FormulaPreviewFieldDescriptor> fields)
        {
            ResetToDefaults(profile);
            if (fields == null)
                return;

            foreach (var field in fields)
            {
                if (field != null)
                    values[field.Key] = field.DefaultValue;
            }
        }

        public FormulaDictionaryEvaluationContext CreateContext(FormulaEditorProfile profile)
        {
            return FormulaEditorPreview.CreateContext(profile, values);
        }

        public FormulaPreviewValueSet ToValueSet(FormulaEditorProfile profile)
        {
            return ToValueSet(profile, null);
        }

        public FormulaPreviewValueSet ToValueSet(
            FormulaEditorProfile profile,
            IReadOnlyList<FormulaPreviewFieldDescriptor> fields)
        {
            var previewValues = new List<FormulaPreviewValue>();
            var includedKeys = new HashSet<string>(StringComparer.Ordinal);

            if (profile != null)
            {
                foreach (var input in profile.PreviewInputs)
                {
                    previewValues.Add(new FormulaPreviewValue(input.Key, GetValue(input)));
                    includedKeys.Add(input.Key);
                }
            }

            if (fields != null)
            {
                foreach (var field in fields)
                {
                    if (field == null || !includedKeys.Add(field.Key))
                        continue;

                    previewValues.Add(new FormulaPreviewValue(field.Key, GetValue(field)));
                }
            }

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

        public static IReadOnlyList<FormulaPreviewFieldDescriptor> CollectPreviewFields(
            FormulaEditorProfile profile,
            IEnumerable<FormulaAsset> formulas,
            bool includeAllProfileInputs)
        {
            var fields = new List<FormulaPreviewFieldDescriptor>();
            if (profile == null)
                return fields;

            var requiredInputKeys = new HashSet<string>(StringComparer.Ordinal);
            var scopedFields = new Dictionary<string, FormulaPreviewFieldDescriptor>(StringComparer.Ordinal);
            var visited = new HashSet<FormulaAsset>();
            if (formulas != null)
            {
                foreach (var formula in formulas)
                    CollectFormulaFields(formula, profile, requiredInputKeys, scopedFields, visited);
            }

            foreach (var input in profile.PreviewInputs)
            {
                if (!includeAllProfileInputs && !requiredInputKeys.Contains(input.Key))
                    continue;

                var category = FormulaEditorLabels.GeneralCategory;
                foreach (var provider in profile.Providers)
                {
                    if (provider.PreviewInputKey == input.Key)
                    {
                        category = provider.Category;
                        break;
                    }
                }

                fields.Add(new FormulaPreviewFieldDescriptor(
                    input.Key,
                    input.DisplayName,
                    category,
                    input.Description,
                    input.Kind,
                    input.DefaultValue));
            }

            foreach (var field in scopedFields.Values)
                fields.Add(field);

            return fields.AsReadOnly();
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

        private static void CollectFormulaFields(
            FormulaAsset formula,
            FormulaEditorProfile profile,
            ISet<string> requiredInputKeys,
            IDictionary<string, FormulaPreviewFieldDescriptor> scopedFields,
            ISet<FormulaAsset> visited)
        {
            if (formula == null || !visited.Add(formula))
                return;

            for (var index = 0; index < formula.StepCount; index++)
            {
                if (!formula.TryGetStep(index, out var step) || step?.Source == null)
                    continue;

                var source = step.Source;
                if (source.SourceType == FormulaValueSourceType.NestedFormula)
                {
                    CollectFormulaFields(
                        source.NestedFormula as FormulaAsset,
                        profile,
                        requiredInputKeys,
                        scopedFields,
                        visited);
                    continue;
                }

                if (source.SourceType != FormulaValueSourceType.Provider
                    || !profile.TryGetProvider(source.ProviderId, out var provider))
                    continue;

                if (!string.IsNullOrEmpty(provider.PreviewInputKey))
                    requiredInputKeys.Add(provider.PreviewInputKey);

                if (source.Parameters == null || source.Parameters.Count == 0)
                    continue;

                var key = GetProviderPreviewInputKey(source.ProviderId, source.Parameters);
                if (scopedFields.ContainsKey(key))
                    continue;

                scopedFields.Add(key, new FormulaPreviewFieldDescriptor(
                    key,
                    BuildScopedFieldName(provider, source.Parameters),
                    provider.Category,
                    provider.Description,
                    provider.PreviewInputKind,
                    provider.PreviewValue));
            }
        }

        private static string BuildScopedFieldName(
            FormulaProviderDescriptor provider,
            IReadOnlyList<FormulaParameter> parameters)
        {
            var parts = new List<string>();
            foreach (var parameter in parameters)
            {
                if (parameter == null)
                    continue;

                FormulaParameterDescriptor descriptor = null;
                foreach (var candidate in provider.Parameters)
                {
                    if (candidate.Key == parameter.Name)
                    {
                        descriptor = candidate;
                        break;
                    }
                }

                var label = descriptor?.DisplayName ?? parameter.Name;
                parts.Add(label + "=" + FormatParameterValue(parameter, descriptor));
            }

            return parts.Count == 0
                ? provider.DisplayName
                : provider.DisplayName + " · " + string.Join("，", parts);
        }

        private static string FormatParameterValue(
            FormulaParameter parameter,
            FormulaParameterDescriptor descriptor)
        {
            switch (parameter.Type)
            {
                case FormulaParameterType.Int:
                    if (descriptor?.EnumType != null)
                        return Enum.GetName(descriptor.EnumType, parameter.IntValue)
                               ?? parameter.IntValue.ToString(CultureInfo.InvariantCulture);
                    return parameter.IntValue.ToString(CultureInfo.InvariantCulture);
                case FormulaParameterType.Float:
                    return parameter.FloatValue.ToString("0.###", CultureInfo.InvariantCulture);
                case FormulaParameterType.Bool:
                    return parameter.BoolValue ? "是" : "否";
                case FormulaParameterType.Object:
                    return parameter.ObjectValue == null ? "未指定" : parameter.ObjectValue.name;
                case FormulaParameterType.String:
                default:
                    return parameter.StringValue ?? string.Empty;
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
