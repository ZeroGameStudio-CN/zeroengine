using System.IO;
using NUnit.Framework;

namespace ZeroEngine.Quest.Tests.Editor
{
    public class QuestEditorSourceTests
    {
        private static string PackageRoot
        {
            get
            {
                const string packagePath = "Packages/com.zerogamestudio.zeroengine.narrative";
                return Directory.Exists(packagePath)
                    ? packagePath
                    : "com.zerogamestudio.zeroengine.narrative";
            }
        }

        [Test]
        public void NarrativeAsmdef_DoesNotDependOnProjectSpecificStacks()
        {
            var asmdef = File.ReadAllText($"{PackageRoot}/Runtime/ZeroEngine.Narrative.asmdef");

            StringAssert.DoesNotContain("Sirenix", asmdef);
            StringAssert.DoesNotContain("Odin", asmdef);
            StringAssert.DoesNotContain("POB", asmdef);
            StringAssert.DoesNotContain("PixelCrushers", asmdef);
            StringAssert.DoesNotContain("Addressables", asmdef);
            StringAssert.DoesNotContain("ES3", asmdef);
            StringAssert.DoesNotContain("Quantum", asmdef);
        }

        [Test]
        public void QuestIdDropdown_UsesProviderCacheOutsideDrawer()
        {
            var provider = File.ReadAllText($"{PackageRoot}/Editor/Quest/QuestEditorQuestIdProvider.cs");
            var registry = File.ReadAllText($"{PackageRoot}/Runtime/Quest/QuestStringDropdownProviderRegistry.cs");
            var drawer = File.ReadAllText($"{PackageRoot}/Editor/Quest/QuestStringDropdownDrawer.cs");

            StringAssert.Contains("CachedQuestIds", provider);
            StringAssert.Contains("CachedQuestIdSet", provider);
            StringAssert.Contains("cacheDirty", provider);
            StringAssert.Contains("EditorApplication.projectChanged", provider);
            StringAssert.Contains("QuestConfigValidator.LoadQuestAssets()", provider);
            StringAssert.Contains("QuestStringDropdownProviderRegistry.GetOptions", drawer);
            StringAssert.Contains("QuestStringDropdownProviderRegistry.Register(QuestStringDropdownKind.QuestId, GetQuestIds)", provider);
            StringAssert.Contains("QuestStringDropdownProviderRegistry.Register", provider);
            StringAssert.Contains("QuestEvents.EntityKilled", registry);
            StringAssert.DoesNotContain("AssetDatabase.FindAssets", provider);
            StringAssert.DoesNotContain("AssetDatabase.FindAssets", registry);
            StringAssert.DoesNotContain("AssetDatabase.FindAssets", drawer);
            StringAssert.DoesNotContain("Sirenix", provider + registry + drawer);
            StringAssert.DoesNotContain("Odin", provider + registry + drawer);
        }

        [Test]
        public void QuestStringDropdownAttributes_CoverQuestAuthoringFields()
        {
            var attributes = File.ReadAllText($"{PackageRoot}/Runtime/Quest/QuestStringDropdownAttribute.cs");
            var questConfig = File.ReadAllText($"{PackageRoot}/Runtime/Quest/Data/QuestConfigSO.cs");
            var questProvider = File.ReadAllText($"{PackageRoot}/Runtime/Quest/QuestProvider.cs");
            var kill = File.ReadAllText($"{PackageRoot}/Runtime/Quest/Conditions/KillCondition.cs");
            var collect = File.ReadAllText($"{PackageRoot}/Runtime/Quest/Conditions/CollectCondition.cs");
            var interact = File.ReadAllText($"{PackageRoot}/Runtime/Quest/Conditions/InteractCondition.cs");
            var reach = File.ReadAllText($"{PackageRoot}/Runtime/Quest/Conditions/ReachCondition.cs");
            var custom = File.ReadAllText($"{PackageRoot}/Runtime/Quest/Conditions/CustomCondition.cs");
            var reward = File.ReadAllText($"{PackageRoot}/Runtime/Quest/Rewards/ItemReward.cs");
            var trigger = File.ReadAllText($"{PackageRoot}/Runtime/Quest/Trigger/CompleteObjectiveAction.cs");

            foreach (var symbol in new[]
                     {
                         "QuestStringDropdownKind",
                         "QuestStringDropdownAttribute",
                         "QuestNpcIdDropdownAttribute",
                         "QuestEntityIdDropdownAttribute",
                         "QuestInteractTargetIdDropdownAttribute",
                         "QuestItemIdDropdownAttribute",
                         "QuestLocationIdDropdownAttribute",
                         "QuestEventNameDropdownAttribute"
                     })
            {
                StringAssert.Contains(symbol, attributes);
            }

            StringAssert.Contains("[QuestNpcIdDropdown]", questConfig);
            StringAssert.Contains("[QuestNpcIdDropdown]", questProvider);
            StringAssert.Contains("[QuestEntityIdDropdown]", kill);
            StringAssert.Contains("[QuestItemIdDropdown]", collect);
            StringAssert.Contains("[QuestInteractTargetIdDropdown]", interact);
            StringAssert.Contains("[QuestLocationIdDropdown]", reach);
            StringAssert.Contains("[QuestEventNameDropdown]", custom);
            StringAssert.Contains("[QuestItemIdDropdown]", reward);
            StringAssert.Contains("[QuestEventNameDropdown]", trigger);
        }

        [Test]
        public void DialogueQuestConditions_AreStructuredAndBackwardCompatible()
        {
            var condition = File.ReadAllText($"{PackageRoot}/Runtime/Dialog/Conditions/DialogQuestCondition.cs");
            var dialogueSo = File.ReadAllText($"{PackageRoot}/Runtime/Dialog/Config/DialogueSO.cs");
            var nodes = File.ReadAllText($"{PackageRoot}/Runtime/Dialog/Nodes/DialogNode.cs");

            StringAssert.Contains("DialogQuestConditionMode", condition);
            StringAssert.Contains("DialogQuestConditionGroup", condition);
            StringAssert.Contains("[QuestIdDropdown]", condition);
            StringAssert.Contains("QuestState.Successful", condition);
            StringAssert.Contains("QuestManager.Instance", condition);
            StringAssert.Contains("public string Condition", dialogueSo);
            StringAssert.Contains("public DialogQuestConditionGroup QuestConditions", dialogueSo);
            StringAssert.Contains("public string Condition", nodes);
            StringAssert.Contains("public DialogQuestConditionGroup QuestConditions", nodes);
            StringAssert.Contains("DialogConditionParser.Evaluate", nodes);
            StringAssert.Contains("opt.QuestConditions.Evaluate()", nodes);
        }

        [Test]
        public void QuestValidator_ReportsBlockingConfigIssues()
        {
            var source = File.ReadAllText($"{PackageRoot}/Editor/Quest/QuestConfigValidator.cs");

            StringAssert.Contains("Quest has no questId", source);
            StringAssert.Contains("QuestId has leading/trailing whitespace", source);
            StringAssert.Contains("Quest has no Conditions", source);
            StringAssert.Contains("QuestValidationSeverity.Error", source);
            StringAssert.Contains("QuestValidationSeverity.Warning", source);
        }

        [Test]
        public void PackageDocumentation_DeclaresAdapterBoundaryAndSaveDeferral()
        {
            var docs = File.ReadAllText($"{PackageRoot}/Documentation~/quest-system.md");

            StringAssert.Contains("IQuestConfigSource", docs);
            StringAssert.Contains("IQuestLocalizationService", docs);
            StringAssert.Contains("IQuestRewardService", docs);
            StringAssert.Contains("Save backend abstraction is not part of this RPG-ready foundation pass", docs);
            StringAssert.Contains("Projects provide adapters", docs);
            StringAssert.Contains("treated as authoritative", docs);
        }
    }
}
