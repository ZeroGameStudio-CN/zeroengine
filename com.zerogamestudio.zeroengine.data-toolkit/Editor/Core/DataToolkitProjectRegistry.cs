using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZGS.DataToolkit.Editor
{
    public static class DataToolkitProjectRegistry
    {
        private static readonly Dictionary<string, Func<DataToolkitProjectProfile>> profileFactories =
            new Dictionary<string, Func<DataToolkitProjectProfile>>(StringComparer.Ordinal);

        private static Func<DataToolkitProjectProfile> defaultProfileFactory;

        internal static event Action DefaultProfileRegistered;
        internal static event Action ProfilesChanged;

        public static void Register(string projectId, Func<DataToolkitProjectProfile> profileFactory)
        {
            if (string.IsNullOrWhiteSpace(projectId))
            {
                throw new ArgumentException("Project id is required.", nameof(projectId));
            }

            profileFactories[projectId.Trim()] = profileFactory ?? throw new ArgumentNullException(nameof(profileFactory));
            ProfilesChanged?.Invoke();
        }

        public static void RegisterDefault(Func<DataToolkitProjectProfile> profileFactory)
        {
            defaultProfileFactory = profileFactory ?? throw new ArgumentNullException(nameof(profileFactory));
            var profile = TryCreateProfile(defaultProfileFactory);
            if (profile != null)
            {
                profileFactories[profile.Settings.ProjectId] = defaultProfileFactory;
            }

            DefaultProfileRegistered?.Invoke();
            ProfilesChanged?.Invoke();
        }

        public static DataToolkitProjectProfile CreateDefaultProfile()
        {
            if (defaultProfileFactory != null)
            {
                var profile = TryCreateProfile(defaultProfileFactory);
                if (profile != null)
                {
                    return profile;
                }
            }

            return CreateGenericProfile();
        }

        public static bool TryCreateProfile(string projectId, out DataToolkitProjectProfile profile)
        {
            profile = null;
            if (string.IsNullOrWhiteSpace(projectId))
            {
                return false;
            }

            return profileFactories.TryGetValue(projectId.Trim(), out var factory) &&
                   (profile = TryCreateProfile(factory)) != null;
        }

        private static DataToolkitProjectProfile TryCreateProfile(Func<DataToolkitProjectProfile> profileFactory)
        {
            try
            {
                return profileFactory?.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return null;
            }
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
