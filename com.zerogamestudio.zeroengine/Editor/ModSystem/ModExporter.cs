#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZeroEngine.AbilitySystem;
using ZeroEngine.BuffSystem;
using ZeroEngine.Inventory;

namespace ZeroEngine.ModSystem.Editor
{
    /// <summary>
    /// Mod内容导出器，将游戏内资源导出为mod格式
    /// </summary>
    public class ModExporter : EditorWindow
    {
        private enum ExportType
        {
            Ability,
            Buff,
            Item
        }
        
        private ExportType _exportType = ExportType.Ability;
        private UnityEngine.Object _assetToExport;
        private string _outputPath = "";
        private bool _includeReferencedAssets = true;
        
        private Vector2 _scrollPos;
        
        [MenuItem("ZeroEngine/Mod System/Export to Mod...", priority = 101)]
        public static void ShowWindow()
        {
            var window = GetWindow<ModExporter>("Export to Mod");
            window.minSize = new Vector2(400, 300);
            window.Show();
        }
        
        [MenuItem("Assets/Export to Mod JSON", priority = 1000)]
        private static void ExportSelectedAsset()
        {
            var selected = Selection.activeObject;
            if (selected == null) return;
            
            var window = GetWindow<ModExporter>("Export to Mod");
            window._assetToExport = selected;
            window.DetectAssetType();
            window.Show();
        }
        
        [MenuItem("Assets/Export to Mod JSON", true)]
        private static bool ValidateExportSelectedAsset()
        {
            var selected = Selection.activeObject;
            return selected is AbilityDataSO || selected is BuffData || selected is InventoryItemSO;
        }
        
        private void OnEnable()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? "";
            _outputPath = Path.Combine(projectRoot, "Mods", "Exported");
        }
        
        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            
            EditorGUILayout.LabelField("Export Asset to Mod", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);
            
            // 资源选择
            EditorGUILayout.LabelField("Source Asset", EditorStyles.boldLabel);
            _exportType = (ExportType)EditorGUILayout.EnumPopup("Asset Type", _exportType);
            
            Type assetType = _exportType switch
            {
                ExportType.Ability => typeof(AbilityDataSO),
                ExportType.Buff => typeof(BuffData),
                ExportType.Item => typeof(InventoryItemSO),
                _ => typeof(UnityEngine.Object)
            };
            
            _assetToExport = EditorGUILayout.ObjectField("Asset", _assetToExport, assetType, false);
            
            EditorGUILayout.Space(10);
            
            // 输出设置
            EditorGUILayout.LabelField("Output Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _outputPath = EditorGUILayout.TextField("Output Folder", _outputPath);
            if (GUILayout.Button("Browse...", GUILayout.Width(80)))
            {
                var path = EditorUtility.OpenFolderPanel("Select Output Folder", _outputPath, "");
                if (!string.IsNullOrEmpty(path))
                {
                    _outputPath = path;
                }
            }
            EditorGUILayout.EndHorizontal();
            
            _includeReferencedAssets = EditorGUILayout.Toggle("Include Referenced Assets", _includeReferencedAssets);
            EditorGUILayout.HelpBox("If enabled, sprites and other referenced assets will be exported to an 'assets' folder.", MessageType.Info);
            
            EditorGUILayout.Space(20);
            
            // 导出按钮
            GUI.enabled = _assetToExport != null && !string.IsNullOrEmpty(_outputPath);
            if (GUILayout.Button("Export", GUILayout.Height(35)))
            {
                Export();
            }
            GUI.enabled = true;
            
            EditorGUILayout.EndScrollView();
        }
        
        private void DetectAssetType()
        {
            if (_assetToExport is AbilityDataSO) _exportType = ExportType.Ability;
            else if (_assetToExport is BuffData) _exportType = ExportType.Buff;
            else if (_assetToExport is InventoryItemSO) _exportType = ExportType.Item;
        }
        
        private void Export()
        {
            try
            {
                Directory.CreateDirectory(_outputPath);
                
                if (_includeReferencedAssets)
                {
                    Directory.CreateDirectory(Path.Combine(_outputPath, "assets"));
                }
                
                var exportedAssets = new List<string>();
                string json = "";
                string fileName = "";
                
                switch (_exportType)
                {
                    case ExportType.Ability:
                        (json, fileName) = ExportAbility((AbilityDataSO)_assetToExport, exportedAssets);
                        break;
                    case ExportType.Buff:
                        (json, fileName) = ExportBuff((BuffData)_assetToExport, exportedAssets);
                        break;
                    case ExportType.Item:
                        (json, fileName) = ExportItem((InventoryItemSO)_assetToExport, exportedAssets);
                        break;
                }
                
                var jsonPath = Path.Combine(_outputPath, fileName);
                File.WriteAllText(jsonPath, json);
                
                var message = $"Exported successfully!\n\nJSON: {jsonPath}";
                if (exportedAssets.Count > 0)
                {
                    message += $"\n\nAssets exported: {exportedAssets.Count}";
                }
                
                EditorUtility.DisplayDialog("Success", message, "OK");
                EditorUtility.RevealInFinder(jsonPath);
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Error", $"Export failed:\n{ex.Message}", "OK");
            }
        }
        
        private (string json, string fileName) ExportAbility(AbilityDataSO ability, List<string> exportedAssets)
        {
            var data = new Dictionary<string, object>
            {
                ["$type"] = "AbilityDataSO",
                ["AbilityName"] = ability.AbilityName,
                ["Description"] = ability.Description
            };
            
            // 导出图标
            if (ability.Icon != null && _includeReferencedAssets)
            {
                var iconPath = ExportSprite(ability.Icon, exportedAssets);
                data["Icon"] = iconPath;
            }
            
            // 导出Triggers
            var triggers = new List<Dictionary<string, object>>();
            foreach (var trigger in ability.Triggers)
            {
                triggers.Add(SerializeComponent(trigger));
            }
            if (triggers.Count > 0) data["Triggers"] = triggers;
            
            // 导出Conditions
            var conditions = new List<Dictionary<string, object>>();
            foreach (var condition in ability.Conditions)
            {
                conditions.Add(SerializeComponent(condition));
            }
            if (conditions.Count > 0) data["Conditions"] = conditions;
            
            // 导出Effects
            var effects = new List<Dictionary<string, object>>();
            foreach (var effect in ability.Effects)
            {
                effects.Add(SerializeComponent(effect));
            }
            if (effects.Count > 0) data["Effects"] = effects;
            
            return (ToJson(data), $"{SanitizeFileName(ability.AbilityName)}.json");
        }
        
        private (string json, string fileName) ExportBuff(BuffData buff, List<string> exportedAssets)
        {
            var data = new Dictionary<string, object>
            {
                ["$type"] = "BuffData",
                ["BuffId"] = buff.BuffId,
                ["Category"] = buff.Category.ToString(),
                ["Duration"] = buff.Duration,
                ["MaxStacks"] = buff.MaxStacks,
                ["TickInterval"] = buff.TickInterval,
                ["ExpireMode"] = buff.ExpireMode.ToString(),
                ["RefreshOnAddStack"] = buff.RefreshOnAddStack,
                ["RefreshOnRemoveStack"] = buff.RefreshOnRemoveStack
            };
            
            if (buff.Icon != null && _includeReferencedAssets)
            {
                data["Icon"] = ExportSprite(buff.Icon, exportedAssets);
            }
            
            if (buff.StatModifiers.Count > 0)
            {
                var modifiers = buff.StatModifiers.Select(m => new Dictionary<string, object>
                {
                    ["StatId"] = m.StatId.Value,
                    ["Value"] = m.Value,
                    ["ModType"] = m.ModType.ToString()
                }).ToList();
                data["StatModifiers"] = modifiers;
            }
            
            return (ToJson(data), $"{SanitizeFileName(buff.BuffId)}.json");
        }
        
        private (string json, string fileName) ExportItem(InventoryItemSO item, List<string> exportedAssets)
        {
            var data = new Dictionary<string, object>
            {
                ["$type"] = "InventoryItemSO",
                ["Id"] = item.Id,
                ["ItemName"] = item.ItemName,
                ["Type"] = item.Type.ToString(),
                ["MaxStack"] = item.MaxStack,
                ["Description"] = item.Description,
                ["BuyPrice"] = item.BuyPrice,
                ["SellPrice"] = item.SellPrice
            };
            
            if (item.Icon != null && _includeReferencedAssets)
            {
                data["Icon"] = ExportSprite(item.Icon, exportedAssets);
            }
            
            return (ToJson(data), $"{SanitizeFileName(item.ItemName)}.json");
        }
        
        private Dictionary<string, object> SerializeComponent(object component)
        {
            var result = new Dictionary<string, object>
            {
                ["$type"] = component.GetType().Name
            };
            
            var type = component.GetType();
            foreach (var field in type.GetFields())
            {
                if (field.IsPublic && !field.IsStatic)
                {
                    var value = field.GetValue(component);
                    if (value != null)
                    {
                        if (value is Enum)
                        {
                            result[field.Name] = value.ToString();
                        }
                        else if (value.GetType().IsPrimitive || value is string)
                        {
                            result[field.Name] = value;
                        }
                    }
                }
            }
            
            return result;
        }
        
        private string ExportSprite(Sprite sprite, List<string> exportedAssets)
        {
            var sourcePath = AssetDatabase.GetAssetPath(sprite);
            var fileName = Path.GetFileName(sourcePath);
            var destPath = Path.Combine(_outputPath, "assets", fileName);
            
            // 复制原始图片文件
            if (File.Exists(sourcePath))
            {
                File.Copy(sourcePath, destPath, true);
                exportedAssets.Add(destPath);
            }
            
            return $"assets/{fileName}";
        }
        
        private string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.Replace(' ', '_').ToLower();
        }
        
        private string ToJson(Dictionary<string, object> data)
        {
            // 简单的JSON序列化
            return FormatJson(SerializeValue(data));
        }
        
        private string SerializeValue(object value)
        {
            if (value == null) return "null";
            if (value is bool b) return b ? "true" : "false";
            if (value is string s) return $"\"{EscapeString(s)}\"";
            if (value is int || value is float || value is double) return value.ToString();
            
            if (value is Dictionary<string, object> dict)
            {
                var pairs = dict.Select(kvp => $"\"{kvp.Key}\": {SerializeValue(kvp.Value)}");
                return "{" + string.Join(", ", pairs) + "}";
            }
            
            if (value is IEnumerable<object> list)
            {
                var items = list.Select(SerializeValue);
                return "[" + string.Join(", ", items) + "]";
            }
            
            if (value is System.Collections.IList ilist)
            {
                var items = new List<string>();
                foreach (var item in ilist)
                {
                    items.Add(SerializeValue(item));
                }
                return "[" + string.Join(", ", items) + "]";
            }
            
            return value.ToString();
        }
        
        private string EscapeString(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
        
        private string FormatJson(string json)
        {
            // 简单格式化
            var result = new System.Text.StringBuilder();
            int indent = 0;
            bool inString = false;
            
            foreach (char c in json)
            {
                if (c == '"' && (result.Length == 0 || result[result.Length - 1] != '\\'))
                {
                    inString = !inString;
                }
                
                if (!inString)
                {
                    if (c == '{' || c == '[')
                    {
                        result.Append(c);
                        result.Append('\n');
                        indent++;
                        result.Append(new string(' ', indent * 4));
                    }
                    else if (c == '}' || c == ']')
                    {
                        result.Append('\n');
                        indent--;
                        result.Append(new string(' ', indent * 4));
                        result.Append(c);
                    }
                    else if (c == ',')
                    {
                        result.Append(c);
                        result.Append('\n');
                        result.Append(new string(' ', indent * 4));
                    }
                    else if (c == ':')
                    {
                        result.Append(": ");
                    }
                    else if (c != ' ')
                    {
                        result.Append(c);
                    }
                }
                else
                {
                    result.Append(c);
                }
            }
            
            return result.ToString();
        }
    }
}
#endif
