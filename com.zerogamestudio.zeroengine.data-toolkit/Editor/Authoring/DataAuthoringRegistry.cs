using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ZGS.DataToolkit.Editor
{
    public static class DataAuthoringRegistry
    {
        private static readonly Dictionary<string, DataAuthoringProfile> Profiles = new Dictionary<string, DataAuthoringProfile>(StringComparer.Ordinal);

        public static IReadOnlyList<DataAuthoringProfile> RegisteredProfiles => Profiles.Values
            .OrderBy(profile => profile.Title, StringComparer.Ordinal)
            .ToArray();

        public static void RefreshFromProviders()
        {
            Profiles.Clear();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    types = exception.Types.Where(type => type != null).ToArray();
                }

                foreach (var type in types)
                {
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    {
                        if (method.GetCustomAttribute<DataAuthoringProviderAttribute>() == null
                            || method.GetParameters().Length != 0
                            || !typeof(DataAuthoringProfile).IsAssignableFrom(method.ReturnType))
                        {
                            continue;
                        }

                        try
                        {
                            Register((DataAuthoringProfile)method.Invoke(null, null));
                        }
                        catch (Exception exception)
                        {
                            Debug.LogException(exception);
                        }
                    }
                }
            }
        }

        public static void Register(DataAuthoringProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.ProfileId))
            {
                return;
            }

            Profiles[profile.ProfileId] = profile;
        }

        public static DataAuthoringProfile GetProfile(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return null;
            }

            if (Profiles.Count == 0)
            {
                RefreshFromProviders();
            }

            return Profiles.TryGetValue(profileId, out var profile) ? profile : null;
        }

        public static void ClearForTests()
        {
            Profiles.Clear();
        }
    }
}
