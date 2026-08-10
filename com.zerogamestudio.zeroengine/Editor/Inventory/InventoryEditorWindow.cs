using UnityEngine;
using UnityEditor;
using System.Linq;
using ZeroEngine.Inventory;

#if ODIN_INSPECTOR
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities;
using Sirenix.Utilities.Editor;
#endif

namespace ZeroEngine.Editor.Inventory
{
#if ODIN_INSPECTOR
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class InventoryEditorWindow : OdinMenuEditorWindow
    {
        [MenuItem("ZeroEngine/Tools/Inventory Editor")]
        private static void OpenWindow()
        {
            var window = GetWindow<InventoryEditorWindow>();
            window.titleContent = new GUIContent("Inventory Editor");
            window.position = GUIHelper.GetEditorWindowRect().AlignCenter(800, 600);
        }

        protected override OdinMenuTree BuildMenuTree()
        {
            var tree = new OdinMenuTree();
            tree.DefaultMenuStyle.IconSize = 28.00f;
            tree.Config.DrawSearchToolbar = true;

            // Add all InventoryItemSO assets found in project
            tree.AddAllAssetsAtPath("Items", "Assets/", typeof(InventoryItemSO), true, true);
            
            // Beautify: Add Icons from the Item itself
            tree.EnumerateTree().AddIcons<InventoryItemSO>(x => x.Icon);
            
            // Sort alphabetically
            tree.SortMenuItemsByName();

            return tree;
        }

        private string _newItemName = "New Item";

        protected override void OnBeginDrawEditors()
        {
            ZeroEngine.EditorUI.EditorUiGUILayout.SectionHeader("Inventory Editor");
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
            // Ensure folder exists
            string folderPath = "Assets/Data/Items";
            if (!System.IO.Directory.Exists(folderPath))
            {
                System.IO.Directory.CreateDirectory(folderPath);
            }

            string path = $"{folderPath}/{name}.asset";
            // Avoid overwriting
            path = AssetDatabase.GenerateUniqueAssetPath(path);

            var newItem = ScriptableObject.CreateInstance<InventoryItemSO>();
            newItem.Id = System.Guid.NewGuid().ToString();
            newItem.ItemName = name; // Set name property if exists, or just filename
            
            AssetDatabase.CreateAsset(newItem, path);
            AssetDatabase.SaveAssets();
            
            // Rebuild and Select
            ForceMenuTreeRebuild();
            this.MenuTree.Selection.Clear();
            var item = AssetDatabase.LoadAssetAtPath<InventoryItemSO>(path);
            this.MenuTree.Selection.Add(this.MenuTree.GetMenuItem(path));
        }
    }
#else
    // Fallback for when Odin is not present
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class InventoryEditorWindow : EditorWindow
    {
        [MenuItem("ZeroEngine/Tools/Inventory Editor")]
        private static void OpenWindow()
        {
            GetWindow<InventoryEditorWindow>("Inventory Editor");
        }

        private void OnGUI()
        {
            ZeroEngine.EditorUI.EditorUiGUILayout.Header("Inventory Editor", "Odin Inspector is required for advanced authoring");
            EditorGUILayout.HelpBox("This Advanced Inventory Editor requires 'Odin Inspector' to be installed.", MessageType.Warning);
            if (GUILayout.Button("Check Plugins"))
            {
                Debug.Log("[ZeroEngine] Please install Odin Inspector for advanced inventory editing.");
            }
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("You can still edit items manually in the Project window.", EditorStyles.wordWrappedLabel);
        }
    }
#endif
}
