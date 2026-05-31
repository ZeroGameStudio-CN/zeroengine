using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace ZeroEngine.AbilitySystem.Editor
{
    public static class AbilityAuthoringRegistry
    {
        private static readonly Dictionary<string, AbilityAuthoringProfile> Profiles = new(StringComparer.OrdinalIgnoreCase);
        private static bool _isRefreshing;

        public static void Register(AbilityAuthoringProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (Profiles.ContainsKey(profile.ProfileId))
            {
                throw new InvalidOperationException($"Ability authoring profile '{profile.ProfileId}' is already registered.");
            }

            Profiles.Add(profile.ProfileId, profile);
        }

        public static AbilityAuthoringProfile GetProfile(string profileId)
        {
            RefreshIfEmpty();
            return Profiles.TryGetValue(profileId ?? string.Empty, out var profile) ? profile : null;
        }

        public static IReadOnlyList<AbilityAuthoringProfile> GetProfiles()
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
                foreach (var method in TypeCache.GetMethodsWithAttribute<AbilityAuthoringProviderAttribute>())
                {
                    var attribute = method.GetCustomAttribute<AbilityAuthoringProviderAttribute>();
                    if (!includeTestProviders && attribute?.TestOnly == true)
                    {
                        continue;
                    }

                    if (!method.IsStatic || method.GetParameters().Length != 0 || method.ReturnType != typeof(AbilityAuthoringProfile))
                    {
                        continue;
                    }

                    var profile = method.Invoke(null, null) as AbilityAuthoringProfile;
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
