using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace ZeroEngine.EditorTools
{
    public static class EditorToolProjectRegistry
    {
        private static readonly Dictionary<string, EditorToolProjectProfile> Profiles = new(StringComparer.OrdinalIgnoreCase);
        private static bool _isRefreshing;

        public static void Register(EditorToolProjectProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (Profiles.ContainsKey(profile.ProjectId))
            {
                throw new InvalidOperationException($"Editor tool profile '{profile.ProjectId}' is already registered.");
            }

            ValidateUniqueIds(profile, profile.AllExecutableCommands(), "command");
            ValidateUniqueIds(profile, profile.Panels, "panel");
            ValidateUniqueIds(profile, profile.TestRunnerTasks, "test runner task");

            Profiles.Add(profile.ProjectId, profile);
        }

        public static bool TryGetProfile(string projectId, out EditorToolProjectProfile profile)
        {
            return Profiles.TryGetValue(projectId ?? string.Empty, out profile);
        }

        public static EditorToolProjectProfile GetProfile(string projectId)
        {
            RefreshIfEmpty();
            return TryGetProfile(projectId, out var profile) ? profile : null;
        }

        public static IReadOnlyList<EditorToolProjectProfile> GetProfiles()
        {
            RefreshIfEmpty();
            return Profiles.Values
                .OrderBy(profile => profile.Title, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static IReadOnlyList<IEditorToolCommand> GetCommands(string projectId)
        {
            return GetProfile(projectId)?.Commands ?? Array.Empty<IEditorToolCommand>();
        }

        public static IReadOnlyList<IEditorToolPanel> GetPanels(string projectId)
        {
            return GetProfile(projectId)?.Panels ?? Array.Empty<IEditorToolPanel>();
        }

        public static IReadOnlyList<IAssetGenerationTask> GetGenerationTasks(string projectId)
        {
            return GetProfile(projectId)?.GenerationTasks ?? Array.Empty<IAssetGenerationTask>();
        }

        public static IReadOnlyList<IValidationTask> GetValidationTasks(string projectId)
        {
            return GetProfile(projectId)?.ValidationTasks ?? Array.Empty<IValidationTask>();
        }

        public static IReadOnlyList<ITestRunnerTask> GetTestRunnerTasks(string projectId)
        {
            return GetProfile(projectId)?.TestRunnerTasks ?? Array.Empty<ITestRunnerTask>();
        }

        public static bool TryGetCommand(string commandId, out IEditorToolCommand command)
        {
            RefreshIfEmpty();
            command = Profiles.Values
                .SelectMany(profile => profile.AllExecutableCommands())
                .FirstOrDefault(candidate => string.Equals(candidate.Id, commandId, StringComparison.OrdinalIgnoreCase));
            return command != null;
        }

        public static EditorToolExecutionResult ExecuteCommand(string commandId)
        {
            return TryGetCommand(commandId, out var command)
                ? command.Execute()
                : EditorToolExecutionResult.Error($"Editor tool command '{commandId}' is not registered.");
        }

        public static void ClearForTests()
        {
            Profiles.Clear();
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
                foreach (var method in TypeCache.GetMethodsWithAttribute<EditorToolProjectProviderAttribute>())
                {
                    var attribute = method.GetCustomAttribute<EditorToolProjectProviderAttribute>();
                    if (!includeTestProviders && attribute?.TestOnly == true)
                    {
                        continue;
                    }

                    if (!method.IsStatic || method.GetParameters().Length != 0 || method.ReturnType != typeof(EditorToolProjectProfile))
                    {
                        continue;
                    }

                    var profile = method.Invoke(null, null) as EditorToolProjectProfile;
                    if (profile == null || Profiles.ContainsKey(profile.ProjectId))
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

        private static void RefreshIfEmpty()
        {
            if (Profiles.Count == 0)
            {
                RefreshFromProviders();
            }
        }

        private static void ValidateUniqueIds<T>(EditorToolProjectProfile profile, IEnumerable<T> items, string itemName)
        {
            var duplicates = items
                .Select(GetId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();

            if (duplicates.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Editor tool profile '{profile.ProjectId}' has duplicate {itemName} id(s): {string.Join(", ", duplicates)}.");
            }
        }

        private static string GetId<T>(T item)
        {
            return item switch
            {
                IEditorToolCommand command => command.Id,
                IEditorToolPanel panel => panel.Id,
                ITestRunnerTask task => task.Id,
                _ => string.Empty
            };
        }
    }
}
