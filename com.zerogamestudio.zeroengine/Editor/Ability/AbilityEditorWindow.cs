using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Collections.Generic;
using ZeroEngine.AbilitySystem;

#if ODIN_INSPECTOR
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities;
using Sirenix.Utilities.Editor;
#endif

namespace ZeroEngine.Editor.AbilitySystem
{
#if ODIN_INSPECTOR
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class AbilityEditorWindow : OdinMenuEditorWindow
    {
        [MenuItem("ZeroEngine/Tools/Ability Editor")]
        private static void OpenWindow()
        {
            var window = GetWindow<AbilityEditorWindow>();
            window.titleContent = new GUIContent("Ability Editor");
            window.position = GUIHelper.GetEditorWindowRect().AlignCenter(800, 600);
        }

        protected override OdinMenuTree BuildMenuTree()
        {
            var tree = new OdinMenuTree();
            tree.DefaultMenuStyle.IconSize = 28.00f;
            tree.Config.DrawSearchToolbar = true;

            tree.AddAllAssetsAtPath("Abilities", "Assets/", typeof(AbilityDataSO), true, true);
            tree.EnumerateTree().AddIcons<AbilityDataSO>(x => x.Icon);
            tree.SortMenuItemsByName();
            
            // Advanced Search: Filter by Name OR Component Type/Content
            tree.Config.SearchFunction = (node) =>
            {
                if (node.Value is AbilityDataSO ability)
                {
                    string search = node.MenuTree.Config.SearchTerm.ToLower();
                    if (string.IsNullOrEmpty(search)) return true;

                    // Match Name
                    if (ability.AbilityName != null && ability.AbilityName.ToLower().Contains(search)) return true;
                    if (node.Name.ToLower().Contains(search)) return true;

                    // Match Content (Deep Search)
                    if (HasComponentMatch(ability.Triggers, search)) return true;
                    if (HasComponentMatch(ability.Effects, search)) return true;
                    if (HasComponentMatch(ability.Conditions, search)) return true;

                    return false;
                }
                return true; // Keep folders? Or default behavior.
            };

            return tree;
        }

        private bool HasComponentMatch<T>(List<T> components, string search)
        {
            if (components == null) return false;
            foreach (var c in components)
            {
                if (c == null) continue;
                // Search Type Name (e.g. "DamageEffect")
                if (c.GetType().Name.ToLower().Contains(search)) return true;
                // Search Fields? (Reflection could go here, but type name is usually enough for "Search Component")
            }
            return false;
        }

        private string _newItemName = "New Ability";

        protected override void OnBeginDrawEditors()
        {
            ZeroEngine.EditorUI.EditorUiGUILayout.SectionHeader("Ability Editor");
            var selected = this.MenuTree.Selection.FirstOrDefault();
            var toolbarHeight = this.MenuTree.Config.SearchToolbarHeight;

            SirenixEditorGUI.BeginHorizontalToolbar(toolbarHeight);
            {
                if (selected != null)
                {
                    GUILayout.Label(selected.Name);
                }

                GUILayout.FlexibleSpace();

                _newItemName = EditorGUILayout.TextField(_newItemName, GUILayout.Width(150));
                
                if (SirenixEditorGUI.ToolbarButton(new GUIContent("Create")))
                {
                    CreateNewItem(_newItemName);
                }
            }
            SirenixEditorGUI.EndHorizontalToolbar();
        }

        private void CreateNewItem(string name)
        {
            string folderPath = "Assets/Data/Abilities";
            if (!System.IO.Directory.Exists(folderPath))
            {
                System.IO.Directory.CreateDirectory(folderPath);
            }

            string path = $"{folderPath}/{name}.asset";
            path = AssetDatabase.GenerateUniqueAssetPath(path);

            var newItem = ScriptableObject.CreateInstance<AbilityDataSO>();
            newItem.AbilityName = name;
            
            AssetDatabase.CreateAsset(newItem, path);
            AssetDatabase.SaveAssets();
            
            ForceMenuTreeRebuild();
            this.MenuTree.Selection.Clear();
            var item = AssetDatabase.LoadAssetAtPath<AbilityDataSO>(path);
            this.MenuTree.Selection.Add(this.MenuTree.GetMenuItem(path));
        }
    }
#else
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class AbilityEditorWindow : EditorWindow
    {
        [MenuItem("ZeroEngine/Tools/Ability Editor")]
        private static void OpenWindow() => GetWindow<AbilityEditorWindow>("Ability Editor");

        private void OnGUI()
        {
            ZeroEngine.EditorUI.EditorUiGUILayout.Header("Ability Editor", "Odin Inspector is required for advanced authoring");
            EditorGUILayout.HelpBox("Requires 'Odin Inspector'.", MessageType.Warning);
        }
    }
#endif
}
