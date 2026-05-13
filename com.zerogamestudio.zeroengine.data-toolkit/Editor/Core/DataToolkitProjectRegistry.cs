using System;
using UnityEngine;

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
                excludedPaths: Array.Empty<string>()));
        }
    }
}
