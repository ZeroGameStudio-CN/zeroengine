using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ZeroEngine.Formula.Tests.Editor")]

namespace ZeroEngine.Formula.Editor
{
    public static class FormulaEditorProfileRegistry
    {
        private static readonly Dictionary<string, FormulaEditorProfile> Profiles = new();
        private static string activeProfileId;

        public static FormulaEditorProfile ActiveProfile
        {
            get
            {
                if (!string.IsNullOrEmpty(activeProfileId)
                    && Profiles.TryGetValue(activeProfileId, out var profile))
                {
                    return profile;
                }

                return FormulaEditorProfile.CreateEmpty("zeroengine.default", "通用公式");
            }
        }

        public static IReadOnlyList<FormulaEditorProfile> RegisteredProfiles => new List<FormulaEditorProfile>(Profiles.Values);

        public static void Register(FormulaEditorProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (string.IsNullOrEmpty(profile.ProfileId))
            {
                throw new ArgumentException("Profile id cannot be empty.", nameof(profile));
            }

            if (Profiles.ContainsKey(profile.ProfileId))
            {
                throw new InvalidOperationException($"Formula editor profile '{profile.ProfileId}' is already registered.");
            }

            Profiles.Add(profile.ProfileId, profile);
        }

        public static void SetActiveProfile(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("Profile id cannot be empty.", nameof(id));
            }

            if (!Profiles.ContainsKey(id))
            {
                throw new InvalidOperationException($"Formula editor profile '{id}' is not registered.");
            }

            activeProfileId = id;
        }

        internal static void ClearForTests()
        {
            Profiles.Clear();
            activeProfileId = null;
        }
    }
}
