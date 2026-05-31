using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZGS.DataToolkit.Editor
{
    public class DataAuthoringLockedField
    {
        public DataAuthoringLockedField(string fieldPath, string displayName, string reason)
        {
            FieldPath = fieldPath ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? FieldPath : displayName;
            Reason = reason ?? string.Empty;
        }

        public string FieldPath { get; }
        public string DisplayName { get; }
        public string Reason { get; }
    }

    public interface IDataAuthoringFieldLockProvider
    {
        string ProviderId { get; }
        IReadOnlyList<DataAuthoringLockedField> GetLockedFields(Type assetType);
    }

    public static class DataAuthoringFieldLockRegistry
    {
        private static readonly List<IDataAuthoringFieldLockProvider> Providers = new();

        public static void Register(IDataAuthoringFieldLockProvider provider)
        {
            if (provider == null)
            {
                return;
            }

            var providerId = string.IsNullOrWhiteSpace(provider.ProviderId)
                ? provider.GetType().FullName
                : provider.ProviderId;
            Providers.RemoveAll(existing => string.Equals(
                string.IsNullOrWhiteSpace(existing.ProviderId) ? existing.GetType().FullName : existing.ProviderId,
                providerId,
                StringComparison.Ordinal));
            Providers.Add(provider);
        }

        public static void ClearForTests()
        {
            Providers.Clear();
        }

        public static bool TryGetLockedField(Type assetType, string fieldPath, out DataAuthoringLockedField lockedField)
        {
            lockedField = null;
            if (assetType == null || string.IsNullOrWhiteSpace(fieldPath))
            {
                return false;
            }

            foreach (var provider in Providers)
            {
                var fields = provider.GetLockedFields(assetType);
                if (fields == null)
                {
                    continue;
                }

                foreach (var field in fields)
                {
                    if (field != null && string.Equals(field.FieldPath, fieldPath, StringComparison.Ordinal))
                    {
                        lockedField = field;
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool ShouldDisableField(Type assetType, string fieldPath, bool hasAssignedValue)
        {
            return hasAssignedValue && TryGetLockedField(assetType, fieldPath, out _);
        }
    }

    public static class DataAuthoringFieldLockUtility
    {
        public static string BuildAssignedValueDisableExpression(string fieldPath, Type fieldType, bool isLocked)
        {
            if (!isLocked || string.IsNullOrWhiteSpace(fieldPath) || fieldType == null)
            {
                return string.Empty;
            }

            if (fieldType == typeof(string))
            {
                return $"@!string.IsNullOrWhiteSpace({fieldPath})";
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(fieldType))
            {
                return $"@{fieldPath} != null";
            }

            return string.Empty;
        }
    }
}
