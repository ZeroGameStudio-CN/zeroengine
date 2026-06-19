using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZeroEngine.Relationship;

namespace ZeroEngine.Social.Editor
{
    public enum SocialValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class SocialValidationIssue
    {
        public SocialValidationIssue(SocialValidationSeverity severity, string assetName, string fieldPath, string message)
        {
            Severity = severity;
            AssetName = assetName;
            FieldPath = fieldPath;
            Message = message;
        }

        public SocialValidationSeverity Severity { get; }
        public string AssetName { get; }
        public string FieldPath { get; }
        public string Message { get; }

        public override string ToString()
        {
            return $"[{Severity}] {AssetName}.{FieldPath}: {Message}";
        }
    }

    public static class SocialConfigValidator
    {
        public static IReadOnlyList<SocialValidationIssue> Validate(
            IEnumerable<RelationshipDataSO> relationships = null,
            IEnumerable<RelationshipGroupSO> groups = null)
        {
            bool loadAll = relationships == null && groups == null;
            var relationshipList = Resolve(relationships, loadAll);
            var groupList = Resolve(groups, loadAll);
            var issues = new List<SocialValidationIssue>();

            AddDuplicateStringIssues(issues, relationshipList, relationship => relationship.NpcId, "NpcId", "NPC ID");
            AddDuplicateStringIssues(issues, groupList, group => group.GroupId, "GroupId", "Relationship group ID");

            foreach (var relationship in relationshipList)
                ValidateRelationship(issues, relationship);
            foreach (var group in groupList)
                ValidateGroup(issues, group);

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

        private static void ValidateRelationship(List<SocialValidationIssue> issues, RelationshipDataSO relationship)
        {
            if (relationship == null)
                return;

            RequireText(issues, relationship, "NpcId", relationship.NpcId, "Relationship data must have a stable NPC ID.");
            RequireText(issues, relationship, "DisplayName", relationship.DisplayName, "Relationship data must have a display name.");
            RequireText(issues, relationship, "Description", relationship.Description, "Relationship data should describe the NPC.", SocialValidationSeverity.Warning);

            if (relationship.MaxGiftsPerDay < 0)
                Add(issues, SocialValidationSeverity.Error, relationship, "MaxGiftsPerDay", "Max gifts per day cannot be negative.");
            if (relationship.MaxTalksPerDay < 0)
                Add(issues, SocialValidationSeverity.Error, relationship, "MaxTalksPerDay", "Max talks per day cannot be negative.");

            ValidateThresholds(issues, relationship);
            ValidateGiftList(issues, relationship, "LikedGifts", relationship.LikedGifts);
            ValidateGiftList(issues, relationship, "DislikedGifts", relationship.DislikedGifts);
            ValidateGiftOverlap(issues, relationship);
            ValidateEvents(issues, relationship);
            ValidateStringList(issues, relationship, "Tags", relationship.Tags, SocialValidationSeverity.Warning);
        }

        private static void ValidateGroup(List<SocialValidationIssue> issues, RelationshipGroupSO group)
        {
            if (group == null)
                return;

            RequireText(issues, group, "GroupId", group.GroupId, "Relationship group must have a stable ID.");
            RequireText(issues, group, "DisplayName", group.DisplayName, "Relationship group must have a display name.");
            RequireText(issues, group, "Description", group.Description, "Relationship group should have a description.", SocialValidationSeverity.Warning);

            var members = group.Members ?? new List<RelationshipDataSO>();
            var ids = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < members.Count; i++)
            {
                var member = members[i];
                if (member == null)
                {
                    Add(issues, SocialValidationSeverity.Error, group, $"Members[{i}]", "Relationship group contains an empty member reference.");
                    continue;
                }

                string id = member.NpcId?.Trim();
                if (!string.IsNullOrEmpty(id) && !ids.Add(id))
                    Add(issues, SocialValidationSeverity.Error, group, $"Members[{i}]", $"Duplicate relationship member '{id}'.");
            }
        }

        private static void ValidateThresholds(List<SocialValidationIssue> issues, RelationshipDataSO relationship)
        {
            var thresholds = relationship.Thresholds ?? new List<RelationshipThreshold>();
            if (thresholds.Count == 0)
            {
                Add(issues, SocialValidationSeverity.Error, relationship, "Thresholds", "Relationship data must define level thresholds.");
                return;
            }

            var levels = new HashSet<RelationshipLevel>();
            int previousPoints = int.MinValue;
            for (int i = 0; i < thresholds.Count; i++)
            {
                var threshold = thresholds[i];
                if (threshold == null)
                {
                    Add(issues, SocialValidationSeverity.Error, relationship, $"Thresholds[{i}]", "Relationship threshold is empty.");
                    continue;
                }

                if (!levels.Add(threshold.Level))
                    Add(issues, SocialValidationSeverity.Error, relationship, $"Thresholds[{i}].Level", $"Duplicate relationship threshold level '{threshold.Level}'.");
                if (threshold.RequiredPoints < 0)
                    Add(issues, SocialValidationSeverity.Error, relationship, $"Thresholds[{i}].RequiredPoints", "Threshold required points cannot be negative.");
                if (threshold.RequiredPoints < previousPoints)
                    Add(issues, SocialValidationSeverity.Error, relationship, $"Thresholds[{i}].RequiredPoints", "Relationship thresholds must be sorted by required points.");
                previousPoints = threshold.RequiredPoints;

                ValidateStringList(issues, relationship, $"Thresholds[{i}].UnlockIds", threshold.UnlockIds, SocialValidationSeverity.Warning);
                ValidateStringList(issues, relationship, $"Thresholds[{i}].UnlockDialogueIds", threshold.UnlockDialogueIds, SocialValidationSeverity.Warning);
            }
        }

        private static void ValidateGiftList(List<SocialValidationIssue> issues, RelationshipDataSO relationship, string fieldPath, List<GiftData> gifts)
        {
            if (gifts == null)
                return;

            var itemIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < gifts.Count; i++)
            {
                var gift = gifts[i];
                if (gift == null)
                {
                    Add(issues, SocialValidationSeverity.Error, relationship, $"{fieldPath}[{i}]", "Gift preference is empty.");
                    continue;
                }

                if (gift.Item == null)
                {
                    Add(issues, SocialValidationSeverity.Error, relationship, $"{fieldPath}[{i}].Item", "Gift preference must reference an item.");
                    continue;
                }

                string id = gift.Item.Id?.Trim();
                if (!string.IsNullOrEmpty(id) && !itemIds.Add(id))
                    Add(issues, SocialValidationSeverity.Error, relationship, $"{fieldPath}[{i}].Item", $"Duplicate gift item '{id}'.");
                if (gift.PointsChange == 0)
                    Add(issues, SocialValidationSeverity.Warning, relationship, $"{fieldPath}[{i}].PointsChange", "Gift preference changes zero relationship points.");
            }
        }

        private static void ValidateGiftOverlap(List<SocialValidationIssue> issues, RelationshipDataSO relationship)
        {
            var liked = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var gift in relationship.LikedGifts ?? new List<GiftData>())
            {
                if (gift?.Item != null && !string.IsNullOrWhiteSpace(gift.Item.Id))
                    liked.Add(gift.Item.Id.Trim());
            }

            foreach (var gift in relationship.DislikedGifts ?? new List<GiftData>())
            {
                if (gift?.Item != null && !string.IsNullOrWhiteSpace(gift.Item.Id) && liked.Contains(gift.Item.Id.Trim()))
                    Add(issues, SocialValidationSeverity.Error, relationship, "DislikedGifts", $"Gift item '{gift.Item.Id.Trim()}' cannot be both liked and disliked.");
            }
        }

        private static void ValidateEvents(List<SocialValidationIssue> issues, RelationshipDataSO relationship)
        {
            var events = relationship.Events ?? new List<RelationshipEvent>();
            var ids = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < events.Count; i++)
            {
                var evt = events[i];
                if (evt == null)
                {
                    Add(issues, SocialValidationSeverity.Error, relationship, $"Events[{i}]", "Relationship event is empty.");
                    continue;
                }

                RequireText(issues, relationship, $"Events[{i}].EventId", evt.EventId, "Relationship event must have a stable ID.");
                RequireText(issues, relationship, $"Events[{i}].DisplayName", evt.DisplayName, "Relationship event must have a display name.");
                if (!string.IsNullOrWhiteSpace(evt.EventId) && !ids.Add(evt.EventId.Trim()))
                    Add(issues, SocialValidationSeverity.Error, relationship, $"Events[{i}].EventId", $"Duplicate relationship event ID '{evt.EventId.Trim()}'.");
                if (evt.RequiredPoints < 0)
                    Add(issues, SocialValidationSeverity.Error, relationship, $"Events[{i}].RequiredPoints", "Relationship event required points cannot be negative.");
                if (string.IsNullOrWhiteSpace(evt.DialogueId))
                    Add(issues, SocialValidationSeverity.Warning, relationship, $"Events[{i}].DialogueId", "Relationship event should reference a dialogue ID.");
                ValidateStringList(issues, relationship, $"Events[{i}].UnlockIds", evt.UnlockIds, SocialValidationSeverity.Warning);
            }
        }

        private static void ValidateStringList(
            List<SocialValidationIssue> issues,
            UnityEngine.Object asset,
            string fieldPath,
            IEnumerable<string> values,
            SocialValidationSeverity emptySeverity)
        {
            if (values == null)
                return;

            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            int index = 0;
            foreach (var value in values)
            {
                string normalized = value?.Trim();
                if (string.IsNullOrEmpty(normalized))
                    Add(issues, emptySeverity, asset, $"{fieldPath}[{index}]", "String entry is empty.");
                else if (!seen.Add(normalized))
                    Add(issues, SocialValidationSeverity.Warning, asset, $"{fieldPath}[{index}]", $"Duplicate string entry '{normalized}'.");
                index++;
            }
        }

        private static void AddDuplicateStringIssues<T>(
            List<SocialValidationIssue> issues,
            IEnumerable<T> assets,
            System.Func<T, string> getId,
            string fieldPath,
            string label)
            where T : UnityEngine.Object
        {
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var asset in assets.Where(asset => asset != null))
            {
                string id = getId(asset)?.Trim();
                if (string.IsNullOrEmpty(id))
                    continue;

                if (!seen.Add(id))
                    Add(issues, SocialValidationSeverity.Error, asset, fieldPath, $"{label} '{id}' is duplicated.");
            }
        }

        private static void RequireText(
            List<SocialValidationIssue> issues,
            UnityEngine.Object asset,
            string fieldPath,
            string value,
            string message,
            SocialValidationSeverity severity = SocialValidationSeverity.Error)
        {
            if (string.IsNullOrWhiteSpace(value))
                Add(issues, severity, asset, fieldPath, message);
        }

        private static void Add(List<SocialValidationIssue> issues, SocialValidationSeverity severity, UnityEngine.Object asset, string fieldPath, string message)
        {
            issues.Add(new SocialValidationIssue(severity, string.IsNullOrEmpty(asset.name) ? asset.GetType().Name : asset.name, fieldPath, message));
        }
    }
}
