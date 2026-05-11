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
            var drawer = File.ReadAllText($"{PackageRoot}/Editor/Quest/QuestIdDropdownDrawer.cs");

            StringAssert.Contains("CachedQuestIds", provider);
            StringAssert.Contains("CachedQuestIdSet", provider);
            StringAssert.Contains("cacheDirty", provider);
            StringAssert.Contains("EditorApplication.projectChanged", provider);
            StringAssert.Contains("QuestConfigValidator.LoadQuestAssets()", provider);
            StringAssert.Contains("QuestEditorQuestIdProvider.GetQuestIds()", drawer);
            StringAssert.DoesNotContain("AssetDatabase.FindAssets", provider);
            StringAssert.DoesNotContain("AssetDatabase.FindAssets", drawer);
            StringAssert.DoesNotContain("Sirenix", provider + drawer);
            StringAssert.DoesNotContain("Odin", provider + drawer);
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
