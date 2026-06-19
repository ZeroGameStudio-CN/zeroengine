using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Inventory;
using ZeroEngine.Relationship;
using ZeroEngine.Social.Editor;
using Object = UnityEngine.Object;

namespace ZeroEngine.Social.Editor.Tests
{
    public sealed class SocialConfigValidatorTests
    {
        [Test]
        public void Validate_ReportsBrokenRelationshipAndGroupConfig()
        {
            var relationshipA = ScriptableObject.CreateInstance<RelationshipDataSO>();
            var relationshipB = ScriptableObject.CreateInstance<RelationshipDataSO>();
            var group = ScriptableObject.CreateInstance<RelationshipGroupSO>();
            var item = ScriptableObject.CreateInstance<InventoryItemSO>();

            try
            {
                item.Id = "gift";

                relationshipA.name = "RelationshipA";
                relationshipA.NpcId = "npc_001";
                relationshipA.DisplayName = string.Empty;
                relationshipA.MaxGiftsPerDay = -1;
                relationshipA.Thresholds.Add(new RelationshipThreshold { Level = RelationshipLevel.Friend, RequiredPoints = 100 });
                relationshipA.Thresholds.Add(new RelationshipThreshold { Level = RelationshipLevel.Friend, RequiredPoints = 50 });
                relationshipA.LikedGifts.Add(new GiftData { Item = item, PointsChange = 10 });
                relationshipA.DislikedGifts.Add(new GiftData { Item = item, PointsChange = -10 });
                relationshipA.Events.Add(new RelationshipEvent { EventId = "event_a", DisplayName = string.Empty, RequiredPoints = -1 });

                relationshipB.name = "RelationshipB";
                relationshipB.NpcId = "npc_001";
                relationshipB.DisplayName = "Duplicate";
                relationshipB.Thresholds.Add(new RelationshipThreshold { Level = RelationshipLevel.Stranger, RequiredPoints = 0 });

                group.name = "InvalidGroup";
                group.GroupId = string.Empty;
                group.Members.Add(relationshipA);
                group.Members.Add(relationshipA);

                var issues = SocialConfigValidator.Validate(new[] { relationshipA, relationshipB }, new[] { group });

                AssertError(issues, "NPC ID 'npc_001' is duplicated.");
                AssertError(issues, "Relationship data must have a display name.");
                AssertError(issues, "Max gifts per day cannot be negative.");
                AssertError(issues, "Duplicate relationship threshold level 'Friend'.");
                AssertError(issues, "Relationship thresholds must be sorted by required points.");
                AssertError(issues, "Gift item 'gift' cannot be both liked and disliked.");
                AssertError(issues, "Relationship event must have a display name.");
                AssertError(issues, "Relationship event required points cannot be negative.");
                AssertError(issues, "Relationship group must have a stable ID.");
                AssertError(issues, "Duplicate relationship member 'npc_001'.");
            }
            finally
            {
                Object.DestroyImmediate(relationshipA);
                Object.DestroyImmediate(relationshipB);
                Object.DestroyImmediate(group);
                Object.DestroyImmediate(item);
            }
        }

        private static void AssertError(IReadOnlyList<SocialValidationIssue> issues, string expectedMessage)
        {
            Assert.That(
                issues.Any(issue => issue.Severity == SocialValidationSeverity.Error && issue.Message == expectedMessage),
                Is.True,
                $"Expected validation error '{expectedMessage}', got:\n{string.Join("\n", issues.Select(issue => issue.ToString()))}");
        }
    }
}
