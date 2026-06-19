using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZGS.DataToolkit.Editor
{
    internal sealed class PackageConfigValidationProvider : IDataToolkitValidationProvider
    {
        private static readonly Lazy<IReadOnlyList<ValidatorMethod>> ValidatorMethods =
            new Lazy<IReadOnlyList<ValidatorMethod>>(DiscoverValidatorMethods);

        public int Order => 5000;

        public IEnumerable<DataValidationIssue> Validate(DataToolkitValidationContext context)
        {
            if (context == null)
            {
                return Array.Empty<DataValidationIssue>();
            }

            var assets = context.Assets ?? Array.Empty<DataValidationAsset>();
            if (assets.Count == 0)
            {
                return Array.Empty<DataValidationIssue>();
            }

            var lookup = new AssetLookup(assets);
            var issues = new List<DataValidationIssue>();
            foreach (var validator in ValidatorMethods.Value)
            {
                if (!validator.HasMatchingAsset(assets))
                {
                    continue;
                }

                AddValidatorIssues(validator, assets, lookup, issues);
            }

            return issues;
        }

        private static void AddValidatorIssues(
            ValidatorMethod validator,
            IReadOnlyList<DataValidationAsset> assets,
            AssetLookup lookup,
            ICollection<DataValidationIssue> issues)
        {
            IEnumerable rawIssues;
            try
            {
                rawIssues = validator.Invoke(assets);
            }
            catch (Exception exception)
            {
                issues.Add(CreateProviderFailureIssue(validator, exception));
                return;
            }

            if (rawIssues == null)
            {
                return;
            }

            try
            {
                foreach (var rawIssue in rawIssues)
                {
                    var issue = ConvertIssue(validator, rawIssue, lookup);
                    if (issue != null)
                    {
                        issues.Add(issue);
                    }
                }
            }
            catch (Exception exception)
            {
                issues.Add(CreateProviderFailureIssue(validator, exception));
            }
        }

        private static DataValidationIssue ConvertIssue(
            ValidatorMethod validator,
            object rawIssue,
            AssetLookup lookup)
        {
            if (rawIssue == null)
            {
                return null;
            }

            var asset = ReadMember(rawIssue, "Asset") as Object;
            var assetName = ReadStringMember(rawIssue, "AssetName");
            var fieldPath = ReadStringMember(rawIssue, "FieldPath");
            var message = ReadStringMember(rawIssue, "Message");
            var severity = ConvertSeverity(ReadMember(rawIssue, "Severity"));

            var contextAsset = lookup.Find(asset, assetName);
            var resolvedAssetName = contextAsset?.AssetName;
            if (string.IsNullOrEmpty(resolvedAssetName))
            {
                resolvedAssetName = !string.IsNullOrEmpty(assetName) ? assetName : GetAssetName(asset);
            }

            var assetPath = contextAsset?.AssetPath;
            if (string.IsNullOrEmpty(assetPath) && asset != null)
            {
                assetPath = AssetDatabase.GetAssetPath(asset);
            }

            var typeName = contextAsset?.Type.Name;
            if (string.IsNullOrEmpty(typeName))
            {
                typeName = asset != null ? asset.GetType().Name : validator.DisplayName;
            }

            return new DataValidationIssue(
                severity,
                typeName,
                resolvedAssetName,
                assetPath,
                fieldPath,
                message);
        }

        private static DataValidationIssue CreateProviderFailureIssue(
            ValidatorMethod validator,
            Exception exception)
        {
            var actualException = exception is TargetInvocationException target && target.InnerException != null
                ? target.InnerException
                : exception;

            return new DataValidationIssue(
                DataValidationSeverity.Error,
                validator.DisplayName,
                string.Empty,
                string.Empty,
                string.Empty,
                $"Package config validator failed: {actualException.Message}");
        }

        private static IReadOnlyList<ValidatorMethod> DiscoverValidatorMethods()
        {
            var validators = new List<ValidatorMethod>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().OrderBy(assembly => assembly.FullName, StringComparer.Ordinal))
            {
                if (!IsZeroEngineAssembly(assembly))
                {
                    continue;
                }

                foreach (var type in GetLoadableTypes(assembly).OrderBy(type => type.FullName, StringComparer.Ordinal))
                {
                    if (!IsStaticConfigValidator(type))
                    {
                        continue;
                    }

                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                                 .Where(method => method.Name == "Validate")
                                 .OrderBy(method => method.ToString(), StringComparer.Ordinal))
                    {
                        if (ValidatorMethod.TryCreate(type, method, out var validator))
                        {
                            validators.Add(validator);
                        }
                    }
                }
            }

            return validators;
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.Where(type => type != null);
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }

        private static bool IsStaticConfigValidator(Type type)
        {
            return type != null
                   && type.IsAbstract
                   && type.IsSealed
                   && !type.ContainsGenericParameters
                   && IsZeroEngineNamespace(type.Namespace)
                   && type.Name.EndsWith("ConfigValidator", StringComparison.Ordinal);
        }

        private static bool IsZeroEngineAssembly(Assembly assembly)
        {
            var assemblyName = assembly?.GetName().Name;
            return StartsWithZeroEnginePrefix(assemblyName);
        }

        private static bool IsZeroEngineNamespace(string namespaceName)
        {
            return StartsWithZeroEnginePrefix(namespaceName);
        }

        private static bool StartsWithZeroEnginePrefix(string value)
        {
            return !string.IsNullOrEmpty(value)
                   && (value.StartsWith("ZeroEngine.", StringComparison.Ordinal)
                       || value.StartsWith("ZGS.", StringComparison.Ordinal));
        }

        private static object ReadMember(object instance, string memberName)
        {
            var type = instance.GetType();
            var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
            {
                return property.GetValue(instance);
            }

            var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(instance);
        }

        private static string ReadStringMember(object instance, string memberName)
        {
            return ReadMember(instance, memberName) as string ?? string.Empty;
        }

        private static DataValidationSeverity ConvertSeverity(object severity)
        {
            switch (severity?.ToString())
            {
                case "Error":
                    return DataValidationSeverity.Error;
                case "Warning":
                    return DataValidationSeverity.Warning;
                default:
                    return DataValidationSeverity.Info;
            }
        }

        private static string GetAssetName(Object asset)
        {
            if (asset == null)
            {
                return string.Empty;
            }

            return string.IsNullOrEmpty(asset.name) ? asset.GetType().Name : asset.name;
        }

        private sealed class ValidatorMethod
        {
            private readonly MethodInfo method;
            private readonly ParameterBinding[] parameters;

            private ValidatorMethod(Type validatorType, MethodInfo method, ParameterBinding[] parameters)
            {
                ValidatorType = validatorType;
                this.method = method;
                this.parameters = parameters;
            }

            public Type ValidatorType { get; }
            public string DisplayName => ValidatorType.Name;

            public static bool TryCreate(Type validatorType, MethodInfo method, out ValidatorMethod validator)
            {
                validator = null;
                if (method == null
                    || method.ContainsGenericParameters
                    || method.GetParameters().Length == 0
                    || !typeof(IEnumerable).IsAssignableFrom(method.ReturnType))
                {
                    return false;
                }

                var parameters = new List<ParameterBinding>();
                foreach (var parameter in method.GetParameters())
                {
                    if (!ParameterBinding.TryCreate(parameter.ParameterType, out var binding))
                    {
                        return false;
                    }

                    parameters.Add(binding);
                }

                validator = new ValidatorMethod(validatorType, method, parameters.ToArray());
                return true;
            }

            public bool HasMatchingAsset(IReadOnlyList<DataValidationAsset> assets)
            {
                return parameters.Any(parameter => parameter.HasMatchingAsset(assets));
            }

            public IEnumerable Invoke(IReadOnlyList<DataValidationAsset> assets)
            {
                var arguments = parameters
                    .Select(parameter => parameter.BuildArray(assets))
                    .ToArray();
                return method.Invoke(null, arguments) as IEnumerable;
            }
        }

        private sealed class ParameterBinding
        {
            private ParameterBinding(Type elementType)
            {
                ElementType = elementType;
            }

            private Type ElementType { get; }

            public static bool TryCreate(Type parameterType, out ParameterBinding binding)
            {
                binding = null;
                var elementType = GetEnumerableElementType(parameterType);
                if (elementType == null || !typeof(Object).IsAssignableFrom(elementType))
                {
                    return false;
                }

                binding = new ParameterBinding(elementType);
                return true;
            }

            public bool HasMatchingAsset(IReadOnlyList<DataValidationAsset> assets)
            {
                return assets.Any(asset => asset?.Asset != null && ElementType.IsInstanceOfType(asset.Asset));
            }

            public Array BuildArray(IReadOnlyList<DataValidationAsset> assets)
            {
                var matchingAssets = assets
                    .Where(asset => asset?.Asset != null && ElementType.IsInstanceOfType(asset.Asset))
                    .Select(asset => asset.Asset)
                    .ToArray();
                var typedArray = Array.CreateInstance(ElementType, matchingAssets.Length);
                for (var index = 0; index < matchingAssets.Length; index++)
                {
                    typedArray.SetValue(matchingAssets[index], index);
                }

                return typedArray;
            }

            private static Type GetEnumerableElementType(Type type)
            {
                if (type == null || type == typeof(string))
                {
                    return null;
                }

                if (type.IsArray)
                {
                    return type.GetElementType();
                }

                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    return type.GetGenericArguments()[0];
                }

                return type.GetInterfaces()
                    .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    .Select(candidate => candidate.GetGenericArguments()[0])
                    .FirstOrDefault();
            }
        }

        private sealed class AssetLookup
        {
            private readonly Dictionary<Object, DataValidationAsset> byObject;
            private readonly Dictionary<string, DataValidationAsset> uniqueByName;

            public AssetLookup(IEnumerable<DataValidationAsset> assets)
            {
                var materializedAssets = (assets ?? Array.Empty<DataValidationAsset>())
                    .Where(asset => asset != null)
                    .ToArray();
                byObject = materializedAssets
                    .Where(asset => asset.Asset != null)
                    .GroupBy(asset => asset.Asset)
                    .ToDictionary(group => group.Key, group => group.First());
                uniqueByName = materializedAssets
                    .Where(asset => !string.IsNullOrEmpty(asset.AssetName))
                    .GroupBy(asset => asset.AssetName, StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() == 1)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            }

            public DataValidationAsset Find(Object asset, string assetName)
            {
                if (asset != null && byObject.TryGetValue(asset, out var contextAsset))
                {
                    return contextAsset;
                }

                if (!string.IsNullOrEmpty(assetName) && uniqueByName.TryGetValue(assetName, out contextAsset))
                {
                    return contextAsset;
                }

                return null;
            }
        }
    }
}
