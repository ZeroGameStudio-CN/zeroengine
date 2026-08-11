using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Editor.Dialog
{
    /// <summary>
    /// Editor tool to export dialogue text to CSV for localization.
    /// </summary>
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class DialogExportWindow : EditorWindow
    {
        private List<ZeroEngine.Dialog.DialogueSO> _dialogues = new List<ZeroEngine.Dialog.DialogueSO>();
        private string _exportPath = "Assets/Localization/Dialog_Export.csv";
        private Vector2 _scrollPos;

        public static void ShowWindow()
        {
            GetWindow<DialogExportWindow>("对话 CSV 导出");
        }

        private void OnGUI()
        {
            ZeroEngine.EditorUI.EditorUiGUILayout.Header("对话 CSV 导出", "查找对话资源并导出供本地化处理的 CSV 文件。");

            using (ZeroEngine.EditorUI.EditorUiGUILayout.Section("导出设置", "选择 CSV 目标；项目内路径会保存为 Assets 相对路径。"))
            {
                EditorGUILayout.BeginHorizontal();
                _exportPath = EditorGUILayout.TextField(new GUIContent("导出路径", "CSV 文件的目标路径。"), _exportPath);
                if (GUILayout.Button(new GUIContent("浏览…", "选择 CSV 保存位置。"), GUILayout.Width(72)))
                {
                    string path = EditorUtility.SaveFilePanel("保存对话 CSV", "Assets", "Dialog_Export", "csv");
                    if (!string.IsNullOrEmpty(path))
                    {
                        _exportPath = path.StartsWith(Application.dataPath)
                            ? "Assets" + path.Substring(Application.dataPath.Length)
                            : path;
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            using (ZeroEngine.EditorUI.EditorUiGUILayout.Section("对话资源", "扫描当前项目中的 DialogueSO，不会修改资源。"))
            {
                if (GUILayout.Button(new GUIContent("查找全部对话资源", "扫描当前项目中的 DialogueSO。")))
                    FindAllDialogues();

                EditorGUILayout.LabelField(new GUIContent($"已找到：{_dialogues.Count} 个 DialogueSO", "当前扫描结果数量。"));

                _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(200));
                foreach (var dialogue in _dialogues)
                    EditorGUILayout.ObjectField(dialogue, typeof(ZeroEngine.Dialog.DialogueSO), false);
                EditorGUILayout.EndScrollView();
            }

            using (new EditorGUI.DisabledScope(_dialogues.Count == 0))
            {
                if (ZeroEngine.EditorUI.EditorUiGUILayout.PrimaryButton(
                        new GUIContent("导出 CSV", "将当前扫描结果写入指定 CSV 文件。"),
                        GUILayout.Height(36)))
                {
                    ExportToCSV();
                }
            }
        }

        private void FindAllDialogues()
        {
            _dialogues.Clear();
            string[] guids = AssetDatabase.FindAssets("t:DialogueSO");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<ZeroEngine.Dialog.DialogueSO>(path);
                if (asset != null)
                    _dialogues.Add(asset);
            }
        }

        private void ExportToCSV()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Asset,EntryIndex,Speaker,Text,LocalizationKey,VoiceKey,ChoiceIndex,ChoiceText,ChoiceLocKey");

            foreach (var dialogue in _dialogues)
            {
                string assetName = dialogue.name;
                for (int i = 0; i < dialogue.Entries.Count; i++)
                {
                    var entry = dialogue.Entries[i];
                    
                    // Main entry line
                    sb.AppendLine($"\"{assetName}\",{i},\"{Escape(entry.Speaker)}\",\"{Escape(entry.Text)}\",\"{entry.LocalizationKey}\",\"{entry.VoiceKey}\",,,");
                    
                    // Choice lines
                    if (entry.Choices != null)
                    {
                        for (int c = 0; c < entry.Choices.Count; c++)
                        {
                            var choice = entry.Choices[c];
                            sb.AppendLine($"\"{assetName}\",{i},,,,,{c},\"{Escape(choice.Text)}\",\"{choice.LocalizationKey}\"");
                        }
                    }
                }
            }

            File.WriteAllText(_exportPath, sb.ToString(), Encoding.UTF8);
            AssetDatabase.Refresh();
            Debug.Log($"[DialogExport] Exported {_dialogues.Count} dialogues to: {_exportPath}");
            EditorUtility.DisplayDialog("导出完成", $"已导出到：\n{_exportPath}", "确定");
        }

        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("\"", "\"\"").Replace("\n", "\\n").Replace("\r", "");
        }
    }
}
