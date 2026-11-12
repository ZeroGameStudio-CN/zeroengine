using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.World.Editor.WorldGraph
{
    public static class WorldAddressablesAuthoringUtility
    {
        private const string AddressablesEditorAssembly = "Unity.Addressables.Editor";
        private const string SettingsDefaultObjectTypeName =
            "UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject, " + AddressablesEditorAssembly;
        private const string BundledAssetGroupSchemaTypeName =
            "UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema, " + AddressablesEditorAssembly;
        private const string ContentUpdateGroupSchemaTypeName =
            "UnityEditor.AddressableAssets.Settings.GroupSchemas.ContentUpdateGroupSchema, " + AddressablesEditorAssembly;
        private const string ModificationEventTypeName =
            "UnityEditor.AddressableAssets.Settings.AddressableAssetSettings+ModificationEvent, " + AddressablesEditorAssembly;

        public static bool RegisterAsset(
            string groupName,
            string assetPath,
            string address,
            string logPrefix = nameof(WorldAddressablesAuthoringUtility))
        {
            if (string.IsNullOrWhiteSpace(groupName)
                || string.IsNullOrWhiteSpace(assetPath)
                || string.IsNullOrWhiteSpace(address))
            {
                Debug.LogWarning($"[{logPrefix}] Addressables registration requires group, asset path, and address.");
                return false;
            }

            var settings = GetAddressableSettings(logPrefix);
            if (settings == null)
            {
                return false;
            }

            var group = FindOrCreateGroup(settings, groupName, logPrefix);
            if (group == null)
            {
                return false;
            }

            EnsureDefaultGroupSchemas(group, logPrefix);

            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrWhiteSpace(guid))
            {
                Debug.LogWarning($"[{logPrefix}] Missing asset guid for {assetPath}.");
                return false;
            }

            var entry = CreateOrMoveEntry(settings, guid, group, logPrefix);
            if (entry == null)
            {
                return false;
            }

            SetEntryAddress(entry, address, logPrefix);
            SetSettingsDirty(settings, entry);
            return true;
        }

        private static object GetAddressableSettings(string logPrefix)
        {
            var type = Type.GetType(SettingsDefaultObjectTypeName);
            var settings = type?.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (settings == null)
            {
                Debug.LogWarning($"[{logPrefix}] AddressableAssetSettings is missing.");
            }

            return settings;
        }

        private static object FindOrCreateGroup(object settings, string groupName, string logPrefix)
        {
            var settingsType = settings.GetType();
            var findGroup = settingsType.GetMethod("FindGroup", new[] { typeof(string) });
            var group = findGroup?.Invoke(settings, new object[] { groupName });
            if (group != null)
            {
                return group;
            }

            var schemaTypes = GetDefaultSchemaTypes(logPrefix);
            var createGroup = settingsType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                    method.Name == "CreateGroup"
                    && method.GetParameters().Length == 6
                    && method.GetParameters()[0].ParameterType == typeof(string));

            if (createGroup == null)
            {
                Debug.LogWarning($"[{logPrefix}] Could not find AddressableAssetSettings.CreateGroup.");
                return null;
            }

            return createGroup.Invoke(
                settings,
                new object[] { groupName, false, false, true, null, schemaTypes });
        }

        private static Type[] GetDefaultSchemaTypes(string logPrefix)
        {
            var schemaTypes = new[]
                {
                    Type.GetType(BundledAssetGroupSchemaTypeName),
                    Type.GetType(ContentUpdateGroupSchemaTypeName)
                }
                .Where(type => type != null)
                .ToArray();

            if (schemaTypes.Length == 0)
            {
                Debug.LogWarning($"[{logPrefix}] Addressables group schema types are unavailable.");
            }

            return schemaTypes;
        }

        private static void EnsureDefaultGroupSchemas(object group, string logPrefix)
        {
            var groupType = group.GetType();
            var getSchema = groupType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                    method.Name == "GetSchema"
                    && method.IsGenericMethodDefinition
                    && method.GetParameters().Length == 0);
            var addSchema = groupType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                    method.Name == "AddSchema"
                    && method.IsGenericMethodDefinition
                    && method.GetParameters().Length == 0);

            if (getSchema == null || addSchema == null)
            {
                Debug.LogWarning($"[{logPrefix}] Could not inspect Addressables group schemas.");
                return;
            }

            foreach (var schemaType in GetDefaultSchemaTypes(logPrefix))
            {
                var existingSchema = getSchema.MakeGenericMethod(schemaType).Invoke(group, null);
                if (existingSchema == null)
                {
                    addSchema.MakeGenericMethod(schemaType).Invoke(group, null);
                }
            }
        }

        private static object CreateOrMoveEntry(object settings, string guid, object group, string logPrefix)
        {
            var method = settings.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(candidate =>
                {
                    if (candidate.Name != "CreateOrMoveEntry")
                    {
                        return false;
                    }

                    var parameters = candidate.GetParameters();
                    return parameters.Length == 4
                           && parameters[0].ParameterType == typeof(string)
                           && parameters[1].ParameterType.IsInstanceOfType(group);
                });

            if (method == null)
            {
                Debug.LogWarning($"[{logPrefix}] Could not find AddressableAssetSettings.CreateOrMoveEntry.");
                return null;
            }

            return method.Invoke(settings, new object[] { guid, group, false, false });
        }

        private static void SetEntryAddress(object entry, string address, string logPrefix)
        {
            var method = entry.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(candidate =>
                    candidate.Name == "SetAddress"
                    && candidate.GetParameters().Length is >= 1 and <= 2
                    && candidate.GetParameters()[0].ParameterType == typeof(string));

            if (method == null)
            {
                Debug.LogWarning($"[{logPrefix}] Could not find AddressableAssetEntry.SetAddress.");
                return;
            }

            var parameters = method.GetParameters();
            var arguments = parameters.Length == 1
                ? new object[] { address }
                : new object[] { address, true };
            method.Invoke(entry, arguments);
        }

        private static void SetSettingsDirty(object settings, object entry)
        {
            var modificationEventType = Type.GetType(ModificationEventTypeName);
            if (modificationEventType == null)
            {
                return;
            }

            var entryMoved = Enum.Parse(modificationEventType, "EntryMoved");
            var method = settings.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(candidate =>
                    candidate.Name == "SetDirty"
                    && candidate.GetParameters().Length is >= 3 and <= 4);

            if (method == null)
            {
                return;
            }

            var parameters = method.GetParameters();
            var arguments = parameters.Length == 3
                ? new[] { entryMoved, entry, true }
                : new[] { entryMoved, entry, true, true };
            method.Invoke(settings, arguments);
        }
    }
}
