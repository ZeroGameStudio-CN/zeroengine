using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace ZGS.DataToolkit.Editor
{
    public static class DataToolkitProjectRegistry
    {
        private static Func<DataToolkitProjectProfile> defaultProfileFactory;

        internal static event Action DefaultProfileRegistered;

        public static void RegisterDefault(Func<DataToolkitProjectProfile> profileFactory)
        {
            defaultProfileFactory = profileFactory;
            DefaultProfileRegistered?.Invoke();
        }

        public static DataToolkitProjectProfile CreateDefaultProfile()
        {
            if (defaultProfileFactory != null)
            {
                try
                {
                    var profile = defaultProfileFactory();
                    if (profile != null)
                    {
                        return profile;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            return CreateGenericProfile();
        }

        private static DataToolkitProjectProfile CreateGenericProfile()
        {
            return new DataToolkitProjectProfile(new DataToolkitProjectSettings(
                projectId: "ZGS",
                windowTitle: "Data Manager",
                menuPath: "Tools/Data Manager",
                editorPrefsPrefix: "ZGS_DataToolkit",
                searchRoots: new[] { "Assets" },
                excludedPaths: Array.Empty<string>()),
                validationProviders: CreateDiscoveredValidationProviders());
        }

        private static IEnumerable<IDataToolkitValidationProvider> CreateDiscoveredValidationProviders()
        {
            foreach (var type in TypeCache.GetTypesDerivedFrom<IDataToolkitValidationProvider>()
                         .OrderBy(type => type.FullName, StringComparer.Ordinal))
            {
                if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters)
                {
                    continue;
                }

                if (type.GetConstructor(
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic,
                        null,
                        Type.EmptyTypes,
                        null) == null)
                {
                    continue;
                }

                IDataToolkitValidationProvider provider = null;
                try
                {
                    provider = (IDataToolkitValidationProvider)Activator.CreateInstance(type, nonPublic: true);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }

                if (provider != null)
                {
                    yield return provider;
                }
            }
        }
    }
}
