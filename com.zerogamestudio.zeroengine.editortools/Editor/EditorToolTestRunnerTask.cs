using System;
using UnityEditor.TestTools.TestRunner.Api;

namespace ZeroEngine.EditorTools
{
    public enum EditorToolTestMode
    {
        EditMode,
        PlayMode
    }

    public sealed class EditorToolTestRunnerTask : ITestRunnerTask
    {
        public EditorToolTestRunnerTask(
            string id,
            string displayName,
            EditorToolTestMode mode,
            string group = "Test Runner",
            int order = 0,
            string tooltip = null,
            string groupDisplayName = null,
            string[] assemblyNames = null,
            string[] categoryNames = null,
            string[] groupNames = null)
        {
            Id = EditorToolCommand.RequireText(id, nameof(id));
            DisplayName = EditorToolCommand.RequireText(displayName, nameof(displayName));
            Tooltip = string.IsNullOrWhiteSpace(tooltip) ? DisplayName : tooltip;
            Group = string.IsNullOrWhiteSpace(group) ? "Test Runner" : group;
            GroupDisplayName = string.IsNullOrWhiteSpace(groupDisplayName) ? Group : groupDisplayName;
            Order = order;
            Mode = mode;
            AssemblyNames = assemblyNames ?? Array.Empty<string>();
            CategoryNames = categoryNames ?? Array.Empty<string>();
            GroupNames = groupNames ?? Array.Empty<string>();
        }

        public string Id { get; }

        public string DisplayName { get; }

        public string Tooltip { get; }

        public string Group { get; }

        public string GroupDisplayName { get; }

        public int Order { get; }

        public EditorToolTestMode Mode { get; }

        public string[] AssemblyNames { get; }

        public string[] CategoryNames { get; }

        public string[] GroupNames { get; }

        public Filter CreateFilter()
        {
            return new Filter
            {
                testMode = Mode == EditorToolTestMode.PlayMode ? TestMode.PlayMode : TestMode.EditMode,
                assemblyNames = AssemblyNames,
                categoryNames = CategoryNames,
                groupNames = GroupNames
            };
        }
    }
}
