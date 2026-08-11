using UnityEditor;
using UnityEngine;

#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities;
using Sirenix.Utilities.Editor;
using System.Linq;
using ZeroEngine.BuffSystem;
#endif

namespace ZeroEngine.Editor.Buff
{
#if ODIN_INSPECTOR
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class BuffEditorWindow : OdinMenuEditorWindow
    {
        internal static void OpenWindow()
        {
            var window = GetWindow<BuffEditorWindow>();
            window.Show();
        }

        protected override OdinMenuTree BuildMenuTree()
        {
            var tree = new OdinMenuTree();
            tree.Config.DrawSearchToolbar = true;
            tree.DefaultMenuStyle = OdinMenuStyle.TreeViewStyle;

            tree.Add("新建 Buff", new CreateNewBuffData());
            tree.AddAllAssetsAtPath("Buff 资源", "Assets/Data/Buffs", typeof(BuffData), true, true);
            
            // Add Icons
            tree.EnumerateTree().Where(x => x.Value is BuffData).ForEach(node =>
            {
                var buff = node.Value as BuffData;
                if (buff != null && buff.Icon != null)
                {
                    node.Icon = buff.Icon.texture;
                }
                else
                {
                    node.Icon = EditorIcons.StarPointer.Active; // Default icon
                }
            });

            tree.SortMenuItemsByName();
            return tree;
        }

        protected override void OnBeginDrawEditors()
        {
            ZeroEngine.EditorUI.EditorUiGUILayout.SectionHeader("Buff 编辑器");
        }

        public class CreateNewBuffData
        {
            [LabelText("Buff 名称")]
            public string BuffName = "NewBuff";

            [Button("创建 Buff")]
            public void Create()
            {
                string path = "Assets/Data/Buffs";
                if (!AssetDatabase.IsValidFolder(path))
                {
                    System.IO.Directory.CreateDirectory(path);
                    AssetDatabase.Refresh();
                }

                var asset = ScriptableObject.CreateInstance<BuffData>();
                asset.BuffId = System.Guid.NewGuid().ToString(); // Auto-generate ID
                
                string fullPath = $"{path}/{BuffName}.asset";
                fullPath = AssetDatabase.GenerateUniqueAssetPath(fullPath);
                
                AssetDatabase.CreateAsset(asset, fullPath);
                AssetDatabase.SaveAssets();

                EditorUtility.FocusProjectWindow();
                Selection.activeObject = asset;
            }
        }
    }
#else
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class BuffEditorWindow : EditorWindow
    {
        internal static void OpenWindow()
        {
            GetWindow<BuffEditorWindow>("Buff 编辑器");
        }

        private void OnGUI()
        {
            ZeroEngine.EditorUI.EditorUiGUILayout.Header("Buff 编辑器", "安装 Odin Inspector 后可使用完整的 Buff 创作界面。");
            EditorGUILayout.HelpBox("高级 Buff 编辑功能需要 Odin Inspector；仍可在 Project 窗口中直接编辑 BuffData 资源。", MessageType.Warning);
        }
    }
#endif
}
