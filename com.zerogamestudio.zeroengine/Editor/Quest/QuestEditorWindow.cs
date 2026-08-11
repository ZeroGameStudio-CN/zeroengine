using UnityEngine;
using UnityEditor;
using System.Linq;
using ZeroEngine.Quest;

#if ODIN_INSPECTOR
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities;
using Sirenix.Utilities.Editor;
#endif

namespace ZeroEngine.Editor.Quest
{
#if ODIN_INSPECTOR
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class QuestEditorWindow : OdinMenuEditorWindow
    {
        internal static void OpenWindow()
        {
            var window = GetWindow<QuestEditorWindow>();
            window.titleContent = new GUIContent("Quest Editor");
            window.position = GUIHelper.GetEditorWindowRect().AlignCenter(800, 600);
        }

        protected override OdinMenuTree BuildMenuTree()
        {
            var tree = new OdinMenuTree();
            tree.DefaultMenuStyle.IconSize = 28.00f;
            tree.Config.DrawSearchToolbar = true;

            tree.AddAllAssetsAtPath("Quests", "Assets/", typeof(QuestConfigSO), true, true);
            
            // Assign a generic 'Flag' icon for quests since they don't have one in SO yet
            tree.EnumerateTree().AddIcon(EditorIcons.Flag);
            tree.SortMenuItemsByName();
            
            return tree;
        }

        private string _newItemName = "New Quest";

        protected override void OnBeginDrawEditors()
        {
            ZeroEngine.EditorUI.EditorUiGUILayout.SectionHeader("Quest Editor");
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
            string folderPath = "Assets/Data/Quests";
            if (!System.IO.Directory.Exists(folderPath))
            {
                System.IO.Directory.CreateDirectory(folderPath);
            }

            string path = $"{folderPath}/{name}.asset";
            path = AssetDatabase.GenerateUniqueAssetPath(path);

            var newItem = ScriptableObject.CreateInstance<QuestConfigSO>();
            newItem.questName = name;
            newItem.questId = System.Guid.NewGuid().ToString();
            
            AssetDatabase.CreateAsset(newItem, path);
            AssetDatabase.SaveAssets();
            
            ForceMenuTreeRebuild();
            this.MenuTree.Selection.Clear();
            var item = AssetDatabase.LoadAssetAtPath<QuestConfigSO>(path);
            this.MenuTree.Selection.Add(this.MenuTree.GetMenuItem(path));
        }
    }
#else
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class QuestEditorWindow : EditorWindow
    {
        internal static void OpenWindow() => GetWindow<QuestEditorWindow>("Quest Editor");

        private void OnGUI()
        {
            ZeroEngine.EditorUI.EditorUiGUILayout.Header("Quest Editor", "Odin Inspector is required for advanced authoring");
            EditorGUILayout.HelpBox("Requires 'Odin Inspector'.", MessageType.Warning);
        }
    }
#endif
}
