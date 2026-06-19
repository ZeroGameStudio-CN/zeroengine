using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZeroEngine.AI.NPCSchedule;
using ZeroEngine.BehaviorTree;

namespace ZeroEngine.AI.Editor
{
    public enum AIValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class AIValidationIssue
    {
        public AIValidationIssue(AIValidationSeverity severity, string assetName, string fieldPath, string message)
        {
            Severity = severity;
            AssetName = assetName;
            FieldPath = fieldPath;
            Message = message;
        }

        public AIValidationSeverity Severity { get; }
        public string AssetName { get; }
        public string FieldPath { get; }
        public string Message { get; }

        public override string ToString()
        {
            return $"[{Severity}] {AssetName}.{FieldPath}: {Message}";
        }
    }

    public static class AIConfigValidator
    {
        public static IReadOnlyList<AIValidationIssue> Validate(
            IEnumerable<NPCScheduleSO> schedules = null,
            IEnumerable<SchedulePresetSO> presets = null,
            IEnumerable<BTTreeAsset> behaviorTrees = null)
        {
            bool loadAll = schedules == null && presets == null && behaviorTrees == null;
            var scheduleList = Resolve(schedules, loadAll);
            var presetList = Resolve(presets, loadAll);
            var treeList = Resolve(behaviorTrees, loadAll);
            var issues = new List<AIValidationIssue>();

            AddDuplicateStringIssues(issues, scheduleList, schedule => schedule.ScheduleId, "ScheduleId", "NPC schedule ID");
            AddDuplicateStringIssues(issues, presetList, preset => preset.PresetId, "PresetId", "Schedule preset ID");

            foreach (var schedule in scheduleList)
                ValidateSchedule(issues, schedule);
            foreach (var preset in presetList)
                ValidatePreset(issues, preset);
            foreach (var tree in treeList)
                ValidateBehaviorTree(issues, tree);

            return issues;
        }

        public static IReadOnlyList<T> LoadAssets<T>() where T : UnityEngine.Object
        {
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(asset => asset != null)
                .ToList();
        }

        private static List<T> Resolve<T>(IEnumerable<T> source, bool loadAll) where T : UnityEngine.Object
        {
            if (source != null)
                return source.ToList();

            return loadAll ? LoadAssets<T>().ToList() : new List<T>();
        }

        private static void ValidateSchedule(List<AIValidationIssue> issues, NPCScheduleSO schedule)
        {
            if (schedule == null)
                return;

            RequireText(issues, schedule, "ScheduleId", schedule.ScheduleId, "NPC schedule must have a stable ID.");
            RequireText(issues, schedule, "DisplayName", schedule.DisplayName, "NPC schedule must have a display name.");
            RequireText(issues, schedule, "Description", schedule.Description, "NPC schedule should have a description.", AIValidationSeverity.Warning);

            if (schedule.DefaultEntry == null)
                Add(issues, AIValidationSeverity.Warning, schedule, "DefaultEntry", "NPC schedule has no default fallback entry.");
            if (schedule.Entries == null || schedule.Entries.Count == 0)
                Add(issues, AIValidationSeverity.Warning, schedule, "Entries", "NPC schedule has no timed entries.");

            ValidateScheduleEntries(issues, schedule, "Entries", schedule.Entries);

            foreach (var message in schedule.Validate())
                Add(issues, AIValidationSeverity.Error, schedule, "Validate()", message);
        }

        private static void ValidatePreset(List<AIValidationIssue> issues, SchedulePresetSO preset)
        {
            if (preset == null)
                return;

            RequireText(issues, preset, "PresetId", preset.PresetId, "Schedule preset must have a stable ID.");
            RequireText(issues, preset, "DisplayName", preset.DisplayName, "Schedule preset must have a display name.");
            RequireText(issues, preset, "Description", preset.Description, "Schedule preset should have a description.", AIValidationSeverity.Warning);
            if (preset.Entries == null || preset.Entries.Count == 0)
                Add(issues, AIValidationSeverity.Warning, preset, "Entries", "Schedule preset has no entries.");

            ValidateScheduleEntries(issues, preset, "Entries", preset.Entries);
        }

        private static void ValidateScheduleEntries(List<AIValidationIssue> issues, UnityEngine.Object asset, string fieldPath, IReadOnlyList<ScheduleEntry> entries)
        {
            if (entries == null)
                return;

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null)
                {
                    Add(issues, AIValidationSeverity.Error, asset, $"{fieldPath}[{i}]", "Schedule entry is empty.");
                    continue;
                }

                RequireText(issues, asset, $"{fieldPath}[{i}].EntryId", entry.EntryId, "Schedule entry must have a stable ID.");
                RequireText(issues, asset, $"{fieldPath}[{i}].Description", entry.Description, "Schedule entry should describe the action.", AIValidationSeverity.Warning);
                if (!string.IsNullOrWhiteSpace(entry.EntryId) && !ids.Add(entry.EntryId.Trim()))
                    Add(issues, AIValidationSeverity.Error, asset, $"{fieldPath}[{i}].EntryId", $"Duplicate schedule entry ID '{entry.EntryId.Trim()}'.");
                if (entry.StartHour < 0f || entry.StartHour > 24f)
                    Add(issues, AIValidationSeverity.Error, asset, $"{fieldPath}[{i}].StartHour", "Schedule entry start hour must be between 0 and 24.");
                if (entry.EndHour < 0f || entry.EndHour > 24f)
                    Add(issues, AIValidationSeverity.Error, asset, $"{fieldPath}[{i}].EndHour", "Schedule entry end hour must be between 0 and 24.");
                if (entry.Action == null)
                    Add(issues, AIValidationSeverity.Error, asset, $"{fieldPath}[{i}].Action", "Schedule entry must define an action.");
            }
        }

        private static void ValidateBehaviorTree(List<AIValidationIssue> issues, BTTreeAsset tree)
        {
            if (tree == null)
                return;

            RequireText(issues, tree, "DisplayName", tree.DisplayName, "Behavior tree must have a display name.");
            RequireText(issues, tree, "Description", tree.Description, "Behavior tree should have a description.", AIValidationSeverity.Warning);
            RequireText(issues, tree, "RootNodeId", tree.RootNodeId, "Behavior tree must define a root node ID.");

            var nodes = tree.Nodes ?? new List<BTNodeData>();
            if (nodes.Count == 0)
                Add(issues, AIValidationSeverity.Error, tree, "Nodes", "Behavior tree must contain at least one node.");

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node == null)
                {
                    Add(issues, AIValidationSeverity.Error, tree, $"Nodes[{i}]", "Behavior tree contains an empty node.");
                    continue;
                }

                RequireText(issues, tree, $"Nodes[{i}].Id", node.Id, "Behavior tree node must have a stable ID.");
                RequireText(issues, tree, $"Nodes[{i}].Name", node.Name, "Behavior tree node must have a display name.");
                if (!string.IsNullOrWhiteSpace(node.Id) && !ids.Add(node.Id.Trim()))
                    Add(issues, AIValidationSeverity.Error, tree, $"Nodes[{i}].Id", $"Duplicate behavior tree node ID '{node.Id.Trim()}'.");

                ValidateBehaviorTreeNodeShape(issues, tree, i, node);
            }

            ValidateBehaviorTreeReferences(issues, tree, nodes, ids);
            RunBehaviorTreeRuntimeValidation(issues, tree, nodes);
        }

        private static void ValidateBehaviorTreeNodeShape(List<AIValidationIssue> issues, BTTreeAsset tree, int index, BTNodeData node)
        {
            bool isComposite = node.Type == BTNodeType.Sequence || node.Type == BTNodeType.Selector || node.Type == BTNodeType.Parallel;
            bool isDecorator = node.Type == BTNodeType.Repeater
                               || node.Type == BTNodeType.Inverter
                               || node.Type == BTNodeType.AlwaysSucceed
                               || node.Type == BTNodeType.AlwaysFail
                               || node.Type == BTNodeType.Conditional;

            if (isComposite && (node.ChildIds == null || node.ChildIds.Count == 0))
                Add(issues, AIValidationSeverity.Warning, tree, $"Nodes[{index}].ChildIds", "Composite behavior tree node has no children.");
            if (isDecorator && (node.ChildIds == null || node.ChildIds.Count != 1))
                Add(issues, AIValidationSeverity.Error, tree, $"Nodes[{index}].ChildIds", "Decorator behavior tree node must have exactly one child.");
            if (node.Type == BTNodeType.Wait && node.WaitDuration <= 0f)
                Add(issues, AIValidationSeverity.Error, tree, $"Nodes[{index}].WaitDuration", "Wait node duration must be positive.");
            if (node.Type == BTNodeType.Log && string.IsNullOrWhiteSpace(node.LogMessage))
                Add(issues, AIValidationSeverity.Warning, tree, $"Nodes[{index}].LogMessage", "Log node should define a log message.");
            if (node.Type == BTNodeType.Repeater && node.RepeatCount == 0)
                Add(issues, AIValidationSeverity.Error, tree, $"Nodes[{index}].RepeatCount", "Repeater repeat count must be -1 for infinite or a positive number.");
        }

        private static void ValidateBehaviorTreeReferences(List<AIValidationIssue> issues, BTTreeAsset tree, List<BTNodeData> nodes, HashSet<string> ids)
        {
            if (!string.IsNullOrWhiteSpace(tree.RootNodeId) && !ids.Contains(tree.RootNodeId.Trim()))
                Add(issues, AIValidationSeverity.Error, tree, "RootNodeId", $"Root node '{tree.RootNodeId.Trim()}' does not exist.");

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node == null || node.ChildIds == null)
                    continue;

                var childIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int childIndex = 0; childIndex < node.ChildIds.Count; childIndex++)
                {
                    string childId = node.ChildIds[childIndex]?.Trim();
                    if (string.IsNullOrEmpty(childId))
                    {
                        Add(issues, AIValidationSeverity.Error, tree, $"Nodes[{i}].ChildIds[{childIndex}]", "Behavior tree child reference is empty.");
                        continue;
                    }

                    if (!ids.Contains(childId))
                        Add(issues, AIValidationSeverity.Error, tree, $"Nodes[{i}].ChildIds[{childIndex}]", $"Behavior tree node references missing child '{childId}'.");
                    if (!childIds.Add(childId))
                        Add(issues, AIValidationSeverity.Warning, tree, $"Nodes[{i}].ChildIds[{childIndex}]", $"Duplicate child reference '{childId}'.");
                    if (string.Equals(node.Id, childId, StringComparison.OrdinalIgnoreCase))
                        Add(issues, AIValidationSeverity.Error, tree, $"Nodes[{i}].ChildIds[{childIndex}]", "Behavior tree node cannot reference itself as a child.");
                }
            }
        }

        private static void RunBehaviorTreeRuntimeValidation(List<AIValidationIssue> issues, BTTreeAsset tree, List<BTNodeData> nodes)
        {
            if (nodes.Any(node => node == null))
                return;

            try
            {
                foreach (var message in tree.Validate())
                    Add(issues, AIValidationSeverity.Error, tree, "Validate()", message);
            }
            catch (Exception exception)
            {
                Add(issues, AIValidationSeverity.Error, tree, "Validate()", $"Runtime validation threw {exception.GetType().Name}: {exception.Message}");
            }
        }

        private static void AddDuplicateStringIssues<T>(
            List<AIValidationIssue> issues,
            IEnumerable<T> assets,
            Func<T, string> getId,
            string fieldPath,
            string label)
            where T : UnityEngine.Object
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in assets.Where(asset => asset != null))
            {
                string id = getId(asset)?.Trim();
                if (string.IsNullOrEmpty(id))
                    continue;

                if (!seen.Add(id))
                    Add(issues, AIValidationSeverity.Error, asset, fieldPath, $"{label} '{id}' is duplicated.");
            }
        }

        private static void RequireText(
            List<AIValidationIssue> issues,
            UnityEngine.Object asset,
            string fieldPath,
            string value,
            string message,
            AIValidationSeverity severity = AIValidationSeverity.Error)
        {
            if (string.IsNullOrWhiteSpace(value))
                Add(issues, severity, asset, fieldPath, message);
        }

        private static void Add(List<AIValidationIssue> issues, AIValidationSeverity severity, UnityEngine.Object asset, string fieldPath, string message)
        {
            issues.Add(new AIValidationIssue(severity, string.IsNullOrEmpty(asset.name) ? asset.GetType().Name : asset.name, fieldPath, message));
        }
    }
}
