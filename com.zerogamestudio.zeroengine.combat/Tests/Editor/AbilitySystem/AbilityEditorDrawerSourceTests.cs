using System.IO;
using NUnit.Framework;

namespace ZeroEngine.AbilitySystem.Editor.Tests
{
    public sealed class AbilityEditorDrawerSourceTests
    {
        private const string DrawerPath =
            "Editor/AbilitySystem/AbilityDefinitionEditorDrawer.cs";
        private const string PickerPath =
            "Editor/AbilitySystem/AbilityComponentPickerDrawer.cs";
        private const string StylesPath =
            "Editor/AbilitySystem/AbilityAuthoringStyles.cs";
        private const string FieldDrawerPath =
            "Editor/AbilitySystem/AbilitySerializedFieldDrawer.cs";
        private const string AbilityDefinitionPath =
            "Runtime/AbilitySystem/AbilityDefinition.cs";
        private const string ComponentDocumentationPath =
            "Runtime/AbilitySystem/AbilityComponentDocumentation.cs";
        private const string SerializedComponentUtilityPath =
            "Editor/AbilitySystem/AbilitySerializedComponentUtility.cs";
        private const string PropertyDrawerPath =
            "Editor/AbilitySystem/AbilityDefinitionPropertyDrawer.cs";

        [Test]
        public void AbilityDefinitionEditorDrawer_ExposesReusableDrawApi()
        {
            var source = File.ReadAllText(AbilityEditorTestPaths.PackageFile(DrawerPath));

            StringAssert.Contains("public static class AbilityDefinitionEditorDrawer", source);
            StringAssert.Contains("public static void Draw(", source);
            StringAssert.Contains("AbilityEditorOptions", source);
            StringAssert.Contains("AbilityEditorValidationUtility.Validate", source);
        }

        [Test]
        public void ComponentPicker_SupportsSearchDocsOrderingAndUndo()
        {
            var source = File.ReadAllText(AbilityEditorTestPaths.PackageFile(PickerPath));

            StringAssert.Contains("AbilityComponentTypeCache.GetComponentTypes", source);
            StringAssert.Contains("AbilitySerializedComponentUtility.AddComponent", source);
            StringAssert.Contains("AbilitySerializedComponentUtility.DuplicateComponent", source);
            StringAssert.Contains("AbilitySerializedComponentUtility.MoveComponent", source);
            StringAssert.Contains("AbilitySerializedComponentUtility.RemoveComponent", source);
            StringAssert.Contains("ExpandedDocs", source);
        }

        [Test]
        public void SerializedComponentUtility_UsesUndoManagedReferenceOperationsAndJsonDuplication()
        {
            var source = File.ReadAllText(AbilityEditorTestPaths.PackageFile(SerializedComponentUtilityPath));

            StringAssert.Contains("public static class AbilitySerializedComponentUtility", source);
            StringAssert.Contains("Undo.RecordObject", source);
            StringAssert.Contains("managedReferenceValue", source);
            StringAssert.Contains("EditorJsonUtility.ToJson", source);
            StringAssert.Contains("EditorJsonUtility.FromJsonOverwrite", source);
            StringAssert.Contains("MoveArrayElement", source);
            StringAssert.Contains("DeleteArrayElementAtIndex", source);
        }

        [Test]
        public void AbilityDefinitionPropertyDrawer_ProvidesPackageFallbackDrawer()
        {
            var source = File.ReadAllText(AbilityEditorTestPaths.PackageFile(PropertyDrawerPath));

            StringAssert.Contains("[CustomPropertyDrawer(typeof(AbilityDefinition))]", source);
            StringAssert.Contains("CreatePropertyGUI", source);
            StringAssert.Contains("IMGUIContainer", source);
            StringAssert.Contains("AbilityDefinitionEditorDrawer.Draw", source);
            StringAssert.Contains("EditorGUI.HelpBox", source);
            StringAssert.Contains("IMGUI custom editors via AbilityDefinitionEditorDrawer.Draw", source);
        }

        [Test]
        public void ComponentPicker_HasSaferUxThanPobBaseline()
        {
            var source = File.ReadAllText(AbilityEditorTestPaths.PackageFile(PickerPath));

            StringAssert.Contains("DuplicateComponent", source);
            StringAssert.Contains("NoMatchingComponents", source);
            StringAssert.Contains("缺失的 managed reference", source);
            StringAssert.Contains("AllowDuplicateComponentTypes", source);
            StringAssert.Contains("options.AllowsComponent", source);
        }

        [Test]
        public void ComponentPicker_UsesCompactChineseActionMenu()
        {
            var source = File.ReadAllText(AbilityEditorTestPaths.PackageFile(PickerPath));

            StringAssert.DoesNotContain("\"Configured\"", source);
            StringAssert.DoesNotContain("\"Duplicate\"", source);
            StringAssert.DoesNotContain("\"Up\"", source);
            StringAssert.DoesNotContain("\"Down\"", source);
            StringAssert.DoesNotContain("\"Remove\"", source);
            StringAssert.DoesNotContain("\"No matching components\"", source);
            StringAssert.Contains("DrawActionsMenu", source);
            StringAssert.Contains("CollapseAddSectionsByDefault", source);
            StringAssert.Contains("ShowComponentActionsInMenu", source);
            StringAssert.Contains("CompactComponentRows", source);
            StringAssert.Contains("AbilitySerializedComponentUtility.DuplicateComponent", source);
            StringAssert.Contains("AbilitySerializedComponentUtility.MoveComponent", source);
            StringAssert.Contains("AbilitySerializedComponentUtility.RemoveComponent", source);
        }

        [Test]
        public void ComponentPicker_UsesProfessionalComponentCardsAndLightEmptyStates()
        {
            var source = File.ReadAllText(AbilityEditorTestPaths.PackageFile(PickerPath));
            var styles = File.ReadAllText(AbilityEditorTestPaths.PackageFile(StylesPath));

            StringAssert.Contains("AbilityAuthoringStyles.DrawPanel", source);
            StringAssert.Contains("DrawComponentHeader", source);
            StringAssert.Contains("DrawEmptyState", source);
            StringAssert.Contains("ComponentHeader", styles);
            StringAssert.Contains("EmptyState", styles);
            StringAssert.DoesNotContain("EditorGUILayout.LabelField(options.Labels.Configured, options.Labels.EmptyConfigured, EditorStyles.miniLabel)", source);
        }

        [Test]
        public void ComponentPicker_ReacquiresSerializedPropertyForDeferredActionMenu()
        {
            var source = File.ReadAllText(AbilityEditorTestPaths.PackageFile(PickerPath));

            StringAssert.Contains("var listPath = listProperty.propertyPath", source);
            StringAssert.Contains("ReacquireListProperty", source);
            StringAssert.Contains("new SerializedObject(owner)", source);
            StringAssert.Contains("FindProperty(listPath)", source);
            StringAssert.DoesNotContain("DuplicateComponent(serializedObject, owner, listProperty, index)", source);
            StringAssert.DoesNotContain("MoveComponent(serializedObject, owner, listProperty, index", source);
            StringAssert.DoesNotContain("RemoveComponent(serializedObject, owner, listProperty, index)", source);
        }

        [Test]
        public void ComponentPicker_UsesChineseFieldDrawerForComponentParameters()
        {
            var source = File.ReadAllText(AbilityEditorTestPaths.PackageFile(PickerPath));

            StringAssert.Contains("参数设置", source);
            StringAssert.Contains("AbilitySerializedFieldDrawer.DrawChildren", source);
            StringAssert.DoesNotContain("EditorGUILayout.PropertyField(element, GUIContent.none, true)", source);
        }

        [Test]
        public void AbilitySerializedFieldDrawer_UsesFieldDocsWithNativeUnityControls()
        {
            var source = File.ReadAllText(AbilityEditorTestPaths.PackageFile(FieldDrawerPath));

            StringAssert.Contains("AbilityFieldDocUtility.GetFieldDoc", source);
            StringAssert.Contains("new GUIContent(doc.DisplayName, doc.Tooltip)", source);
            StringAssert.Contains("EditorGUILayout.PropertyField(child, content, true)", source);
            StringAssert.Contains("HideInInspector", source);
        }

        [Test]
        public void AbilitySerializedFieldDrawer_RestoresIndentWhenUnityGuiExitsEarly()
        {
            var source = File.ReadAllText(AbilityEditorTestPaths.PackageFile(FieldDrawerPath));

            StringAssert.Contains("try", source);
            StringAssert.Contains("finally", source);
            StringAssert.Contains("EditorGUI.indentLevel--", source);
        }

        [Test]
        public void ComponentPicker_HidesParameterFoldoutWhenComponentHasNoVisibleFields()
        {
            var source = File.ReadAllText(AbilityEditorTestPaths.PackageFile(PickerPath));
            var fieldDrawer = File.ReadAllText(AbilityEditorTestPaths.PackageFile(FieldDrawerPath));

            StringAssert.Contains("AbilitySerializedFieldDrawer.HasVisibleChildren", source);
            StringAssert.Contains("if (AbilitySerializedFieldDrawer.HasVisibleChildren(element))", source);
            StringAssert.Contains("public static bool HasVisibleChildren", fieldDrawer);
        }

        [Test]
        public void AbilityFieldDocs_CoverCurrentAbilityAuthoringFields()
        {
            var docs = File.ReadAllText(AbilityEditorTestPaths.PackageFile(ComponentDocumentationPath));
            var ability = File.ReadAllText(AbilityEditorTestPaths.PackageFile(AbilityDefinitionPath));

            StringAssert.Contains("public sealed class AbilityFieldDocAttribute", docs);
            StringAssert.Contains("public static class AbilityFieldDocUtility", docs);
            StringAssert.Contains("[AbilityFieldDoc(\"技能 ID\"", ability);
            StringAssert.Contains("[AbilityFieldDoc(\"目标模式\"", ability);
            StringAssert.Contains("[AbilityFieldDoc(\"资源消耗\"", ability);
            StringAssert.Contains("[AbilityFieldDoc(\"冷却回合\"", ability);
            StringAssert.Contains("[AbilityFieldDoc(\"目标关系\"", ability);
            StringAssert.Contains("[AbilityFieldDoc(\"通过概率\"", ability);
            StringAssert.Contains("[AbilityFieldDoc(\"Buff 资产\"", ability);
            StringAssert.Contains("[AbilityFieldDoc(\"伤害威力\"", ability);
            StringAssert.Contains("[AbilityFieldDoc(\"攻击段数\"", ability);
            StringAssert.Contains("[AbilityFieldDoc(\"法术攻击\"", ability);
            StringAssert.Contains("[AbilityFieldDoc(\"破盾值\"", ability);
            StringAssert.Contains("[AbilityFieldDoc(\"治疗威力\"", ability);
            StringAssert.Contains("[AbilityFieldDoc(\"持续回合\"", ability);
            StringAssert.Contains("[AbilityFieldDoc(\"净化可驱散 Buff\"", ability);
        }
    }
}
