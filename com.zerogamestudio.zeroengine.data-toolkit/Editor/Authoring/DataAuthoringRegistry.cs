using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace ZGS.DataToolkit.Editor
{
    public static class DataAuthoringRegistry
    {
        private static readonly Dictionary<string, DataAuthoringProfile> Profiles = new(StringComparer.OrdinalIgnoreCase);
        private static bool _isRefreshing;

        public static void Register(DataAuthoringProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (Profiles.ContainsKey(profile.ProfileId))
            {
                throw new InvalidOperationException($"Data authoring profile '{profile.ProfileId}' is already registered.");
            }

            Profiles.Add(profile.ProfileId, profile);
        }

        public static DataAuthoringProfile GetProfile(string profileId)
        {
            RefreshIfEmpty();
            return Profiles.TryGetValue(profileId ?? string.Empty, out var profile) ? profile : null;
        }

        public static IReadOnlyList<DataAuthoringProfile> GetProfiles()
        {
            RefreshIfEmpty();
            return Profiles.Values
                .OrderBy(profile => profile.Title, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static void RefreshFromProviders(bool includeTestProviders = false)
        {
            if (_isRefreshing)
            {
                return;
            }

            _isRefreshing = true;
            try
            {
                foreach (var method in TypeCache.GetMethodsWithAttribute<DataAuthoringProviderAttribute>())
                {
                    var attribute = method.GetCustomAttribute<DataAuthoringProviderAttribute>();
                    if (!includeTestProviders && attribute?.TestOnly == true)
                    {
                        continue;
                    }

                    if (!method.IsStatic || method.GetParameters().Length != 0 || method.ReturnType != typeof(DataAuthoringProfile))
                    {
                        continue;
                    }

                    var profile = method.Invoke(null, null) as DataAuthoringProfile;
                    if (profile == null || Profiles.ContainsKey(profile.ProfileId))
                    {
                        continue;
                    }

                    Register(profile);
                }
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        public static void ClearForTests()
        {
            Profiles.Clear();
        }

        private static void RefreshIfEmpty()
        {
            if (Profiles.Count == 0)
            {
                RefreshFromProviders();
            }
        }
    }
}
