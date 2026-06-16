using System.IO;
using NUnit.Framework;

namespace ZeroEngine.AbilitySystem.Editor.Tests
{
    public sealed class AbilityAuthoringWindowSourceTests
    {
        private const string WindowPath = "Editor/AbilitySystem/AbilityAuthoringWindow.cs";
        private const string StylesPath = "Editor/AbilitySystem/AbilityAuthoringStyles.cs";
        private const string RegistryPath = "Editor/AbilitySystem/AbilityAuthoringRegistry.cs";
        private const string EditorAssemblyPath = "Editor/AbilitySystem/ZeroEngine.Combat.Editor.asmdef";

        [Test]
        public void AbilityAuthoringWindow_UsesSharedAbilityDefinitionDrawer()
        {
            var source = File.ReadAllText(AbilityEditorTestPaths.PackageFile(WindowPath));

            StringAssert.Contains("ZGS/Ability/Ability Workbench", source);
            StringAssert.Contains("AbilityDefinitionEditorDrawer.Draw", source);
            StringAssert.Contains("全部校验", source);
            StringAssert.Contains("新建", source);
            StringAssert.Contains("复制", source);
        }

        [Test]
        public void AbilityAuthoringWindow_UsesCompactChineseWorkbenchChrome()
        {
            var source = File.ReadAllText(AbilityEditorTestPaths.PackageFile(WindowPath));

            StringAssert.DoesNotContain("[OK]", source);
            StringAssert.DoesNotContain("Create a new ability asset", source);
            StringAssert.DoesNotContain("Duplicate the selected ability asset", source);
            StringAssert.DoesNotContain("Validate All assets", source);
            StringAssert.Contains("DrawStatusMarker", source);
            StringAssert.Contains("DrawSectionHeader", source);
            StringAssert.Contains("DrawAbilitySummaryChips", source);
            StringAssert.Contains("CompactComponentRows = true", source);
            StringAssert.Contains("CollapseAddSectionsByDefault = true", source);
            StringAssert.Contains("ShowComponentActionsInMenu = true", source);
        }

        [Test]
        public void AbilityAuthoringWindow_UsesProfessionalToolStyling()
        {
            var source = File.ReadAllText(AbilityEditorTestPaths.PackageFile(WindowPath));
            var styles = File.ReadAllText(AbilityEditorTestPaths.PackageFile(StylesPath));

            StringAssert.Contains("AbilityAuthoringStyles", source);
            StringAssert.Contains("DrawAssetCard", source);
            StringAssert.Contains("DrawSelectedAssetHeader", source);
            StringAssert.Contains("DrawStatusPill", source);
            StringAssert.Contains("AbilityAuthoringStyles.DrawPanel", source);
            StringAssert.Contains("AssetCardHeight", styles);
            StringAssert.Contains("SectionHeader", styles);
            StringAssert.Contains("Chip", styles);
            StringAssert.DoesNotContain("var style = selected ? EditorStyles.helpBox : GUIStyle.none", source);
        }

        [Test]
        public void AbilityAuthoringWindow_DrawsStatusPillMarkerAfterPillBackground()
        {
            var source = File.ReadAllText(AbilityEditorTestPaths.PackageFile(WindowPath));

            var labelIndex = source.IndexOf("GUI.Label(rect, text, AbilityAuthoringStyles.Pill)", System.StringComparison.Ordinal);
            var markerIndex = source.IndexOf("EditorGUI.DrawRect(markerRect, AbilityAuthoringStyles.StatusColor(result.Status))", System.StringComparison.Ordinal);

            Assert.That(labelIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(markerIndex, Is.GreaterThan(labelIndex));
        }

        [Test]
        public void AbilityAuthoringWindow_GuardsProfileSwitchAndProviderValidation()
        {
            var source = File.ReadAllText(AbilityEditorTestPaths.PackageFile(WindowPath));

            StringAssert.Contains("HandleProfileChanged", source);
            StringAssert.Contains("ResetSelectedAsset", source);
            StringAssert.Contains("IsSelectedAssetInCurrentProfile", source);
            StringAssert.Contains("SafeValidateAsset", source);
            StringAssert.Contains("catch (System.Exception", source);
        }

        [Test]
        public void AbilityAuthoringWindow_ShowsStructuredBatchGovernanceReport()
        {
            var source = File.ReadAllText(AbilityEditorTestPaths.PackageFile(WindowPath));

            StringAssert.Contains("AbilityBatchValidationRecord", source);
            StringAssert.Contains("_batchRecords", source);
            StringAssert.Contains("DrawBatchGovernance", source);
            StringAssert.Contains("批量治理", source);
            StringAssert.Contains("全部", source);
            StringAssert.Contains("错误", source);
            StringAssert.Contains("警告", source);
            StringAssert.Contains("有效", source);
        }

        [Test]
        public void AbilityAuthoringWindow_BatchGovernanceCanSelectAssetsWithoutMutatingThem()
        {
            var source = File.ReadAllText(AbilityEditorTestPaths.PackageFile(WindowPath));

            StringAssert.Contains("SelectBatchRecord", source);
            StringAssert.Contains("_selectedAsset = record.Asset", source);
            StringAssert.Contains("profile.Adapter.ValidateAsset(record.Asset)", source);
            StringAssert.DoesNotContain("profile.Adapter.CreateAsset();\n            _batchRecords", source);
            StringAssert.Contains("SaveSelectedAsset", source);
            StringAssert.Contains("EditorGUIUtility.PingObject(_selectedAsset)", source);
        }

        [Test]
        public void AbilityAuthoringRegistry_FiltersTestOnlyProvidersByDefault()
        {
            var source = File.ReadAllText(AbilityEditorTestPaths.PackageFile(RegistryPath));

            StringAssert.Contains("includeTestProviders", source);
            StringAssert.Contains("TestOnly", source);
            StringAssert.Contains("TypeCache.GetMethodsWithAttribute<AbilityAuthoringProviderAttribute>", source);
        }

        [Test]
        public void CombatEditorAssembly_DoesNotReferenceP5()
        {
            var source = File.ReadAllText(AbilityEditorTestPaths.PackageFile(EditorAssemblyPath));

            StringAssert.DoesNotContain("ZGS.", source);
            StringAssert.DoesNotContain("SkillData", source);
        }
    }
}
