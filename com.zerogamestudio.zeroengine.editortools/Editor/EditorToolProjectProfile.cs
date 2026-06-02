using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeroEngine.EditorTools
{
    public sealed class EditorToolProjectProfile
    {
        public EditorToolProjectProfile(
            string projectId,
            string title,
            string menuPath = "ZGS/Editor Tools",
            string description = null,
            IEnumerable<IEditorToolCommand> commands = null,
            IEnumerable<IEditorToolPanel> panels = null,
            IEnumerable<IAssetGenerationTask> generationTasks = null,
            IEnumerable<IValidationTask> validationTasks = null,
            IEnumerable<ITestRunnerTask> testRunnerTasks = null)
        {
            ProjectId = RequireText(projectId, nameof(projectId));
            Title = RequireText(title, nameof(title));
            MenuPath = string.IsNullOrWhiteSpace(menuPath) ? "ZGS/Editor Tools" : menuPath;
            Description = string.IsNullOrWhiteSpace(description) ? Title : description;
            Commands = Sort(commands);
            Panels = Sort(panels);
            GenerationTasks = Sort(generationTasks);
            ValidationTasks = Sort(validationTasks);
            TestRunnerTasks = Sort(testRunnerTasks);
        }

        public string ProjectId { get; }

        public string Title { get; }

        public string MenuPath { get; }

        public string Description { get; }

        public IReadOnlyList<IEditorToolCommand> Commands { get; }

        public IReadOnlyList<IEditorToolPanel> Panels { get; }

        public IReadOnlyList<IAssetGenerationTask> GenerationTasks { get; }

        public IReadOnlyList<IValidationTask> ValidationTasks { get; }

        public IReadOnlyList<ITestRunnerTask> TestRunnerTasks { get; }

        internal IEnumerable<IEditorToolCommand> AllExecutableCommands()
        {
            return Commands
                .Concat<IEditorToolCommand>(GenerationTasks)
                .Concat(ValidationTasks);
        }

        private static IReadOnlyList<T> Sort<T>(IEnumerable<T> items) where T : class
        {
            if (items == null)
            {
                return Array.Empty<T>();
            }

            return items
                .Where(item => item != null)
                .OrderBy(GetGroup)
                .ThenBy(GetOrder)
                .ThenBy(GetDisplayName)
                .ToArray();
        }

        private static string GetGroup<T>(T item)
        {
            return item switch
            {
                IEditorToolCommand command => command.Group,
                IEditorToolPanel panel => panel.Group,
                ITestRunnerTask task => task.Group,
                _ => string.Empty
            };
        }

        private static int GetOrder<T>(T item)
        {
            return item switch
            {
                IEditorToolCommand command => command.Order,
                IEditorToolPanel panel => panel.Order,
                ITestRunnerTask task => task.Order,
                _ => 0
            };
        }

        private static string GetDisplayName<T>(T item)
        {
            return item switch
            {
                IEditorToolCommand command => command.DisplayName,
                IEditorToolPanel panel => panel.DisplayName,
                ITestRunnerTask task => task.DisplayName,
                _ => string.Empty
            };
        }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Value cannot be empty.", parameterName);
            }

            return value;
        }
    }
}
