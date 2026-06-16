#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.ModSystem.Editor
{
    /// <summary>
    /// Mod验证工具窗口
    /// </summary>
    public class ModValidatorWindow : EditorWindow
    {
        private string _modsFolder = "";
        private Vector2 _scrollPos;
        private List<ModValidationResult> _results = new();
        private bool _isValidating = false;
        
        [MenuItem("ZeroEngine/Mod System/Validate Mods...", priority = 102)]
        public static void ShowWindow()
        {
            var window = GetWindow<ModValidatorWindow>("Mod Validator");
            window.minSize = new Vector2(500, 400);
            window.Show();
        }
        
        private void OnEnable()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? "";
            _modsFolder = Path.Combine(projectRoot, "Mods");
        }
        
        private void OnGUI()
        {
            EditorGUILayout.LabelField("Mod Validator", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);
            
            // Mods文件夹选择
            EditorGUILayout.BeginHorizontal();
            _modsFolder = EditorGUILayout.TextField("Mods Folder", _modsFolder);
            if (GUILayout.Button("Browse...", GUILayout.Width(80)))
            {
                var path = EditorUtility.OpenFolderPanel("Select Mods Folder", _modsFolder, "");
                if (!string.IsNullOrEmpty(path))
                {
                    _modsFolder = path;
                }
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(10);
            
            // 验证按钮
            GUI.enabled = !_isValidating && Directory.Exists(_modsFolder);
            if (GUILayout.Button("Validate All Mods", GUILayout.Height(30)))
            {
                ValidateMods();
            }
            GUI.enabled = true;
            
            EditorGUILayout.Space(10);
            
            // 结果显示
            if (_results.Count > 0)
            {
                EditorGUILayout.LabelField($"Results ({_results.Count} mods)", EditorStyles.boldLabel);
                
                _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
                
                foreach (var result in _results)
                {
                    DrawValidationResult(result);
                }
                
                EditorGUILayout.EndScrollView();
            }
        }
        
        private void DrawValidationResult(ModValidationResult result)
        {
            var bgColor = result.IsValid ? new Color(0.2f, 0.4f, 0.2f) : new Color(0.4f, 0.2f, 0.2f);
            var style = new GUIStyle(EditorStyles.helpBox);
            
            EditorGUILayout.BeginVertical(style);
            
            // 标题行
            EditorGUILayout.BeginHorizontal();
            var icon = result.IsValid ? "✓" : "✗";
            var statusColor = result.IsValid ? "green" : "red";
            EditorGUILayout.LabelField($"{icon} {result.ModName}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(result.Path, EditorStyles.miniLabel, GUILayout.Width(200));
            EditorGUILayout.EndHorizontal();
            
            // 错误和警告
            if (result.Errors.Count > 0)
            {
                EditorGUILayout.LabelField("Errors:", EditorStyles.boldLabel);
                foreach (var error in result.Errors)
                {
                    EditorGUILayout.HelpBox(error, MessageType.Error);
                }
            }
            
            if (result.Warnings.Count > 0)
            {
                EditorGUILayout.LabelField("Warnings:", EditorStyles.boldLabel);
                foreach (var warning in result.Warnings)
                {
                    EditorGUILayout.HelpBox(warning, MessageType.Warning);
                }
            }
            
            if (result.IsValid && result.Errors.Count == 0 && result.Warnings.Count == 0)
            {
                EditorGUILayout.HelpBox("Mod is valid!", MessageType.Info);
            }
            
            // 打开文件夹按钮
            if (GUILayout.Button("Open Folder", GUILayout.Width(100)))
            {
                EditorUtility.RevealInFinder(result.Path);
            }
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
        }
        
        private void ValidateMods()
        {
            _results.Clear();
            
            if (!Directory.Exists(_modsFolder))
            {
                EditorUtility.DisplayDialog("Error", "Mods folder does not exist.", "OK");
                return;
            }
            
            var modFolders = Directory.GetDirectories(_modsFolder);
            
            foreach (var modFolder in modFolders)
            {
                var result = ValidateMod(modFolder);
                _results.Add(result);
            }
            
            // 按有效性排序（无效的排在前面）
            _results = _results.OrderBy(r => r.IsValid).ThenBy(r => r.ModName).ToList();
            
            var validCount = _results.Count(r => r.IsValid);
            var invalidCount = _results.Count - validCount;
            
            EditorUtility.DisplayDialog("Validation Complete", 
                $"Validated {_results.Count} mods.\n\nValid: {validCount}\nInvalid: {invalidCount}", "OK");
        }
        
        private ModValidationResult ValidateMod(string modPath)
        {
            var result = new ModValidationResult
            {
                Path = modPath,
                ModName = Path.GetFileName(modPath)
            };
            
            // 检查manifest.json
            var manifestPath = Path.Combine(modPath, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                result.Errors.Add("Missing manifest.json");
                return result;
            }
            
            // 解析manifest
            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonUtility.FromJson<ModManifest>(json);
                
                if (manifest == null)
                {
                    result.Errors.Add("Failed to parse manifest.json");
                    return result;
                }
                
                result.ModName = manifest.Name ?? result.ModName;
                
                // 验证必填字段
                if (string.IsNullOrWhiteSpace(manifest.Id))
                {
                    result.Errors.Add("Manifest missing required field: Id");
                }
                
                if (string.IsNullOrWhiteSpace(manifest.Name))
                {
                    result.Errors.Add("Manifest missing required field: Name");
                }
                
                if (string.IsNullOrWhiteSpace(manifest.Version))
                {
                    result.Errors.Add("Manifest missing required field: Version");
                }
                
                // 验证版本格式
                if (!string.IsNullOrEmpty(manifest.Version) && 
                    !System.Text.RegularExpressions.Regex.IsMatch(manifest.Version, @"^\d+\.\d+\.\d+"))
                {
                    result.Warnings.Add($"Version '{manifest.Version}' does not follow semantic versioning (x.y.z)");
                }
                
                // 检查内容目录
                if (manifest.ContentPaths != null)
                {
                    foreach (var contentPath in manifest.ContentPaths)
                    {
                        var fullPath = Path.Combine(modPath, contentPath);
                        if (!Directory.Exists(fullPath))
                        {
                            result.Warnings.Add($"Content path does not exist: {contentPath}");
                        }
                    }
                }
                
                // 验证JSON内容文件
                ValidateContentFiles(modPath, result);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error parsing manifest: {ex.Message}");
            }
            
            return result;
        }
        
        private void ValidateContentFiles(string modPath, ModValidationResult result)
        {
            var jsonFiles = Directory.GetFiles(modPath, "*.json", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith("manifest.json"));
            
            foreach (var jsonFile in jsonFiles)
            {
                try
                {
                    var json = File.ReadAllText(jsonFile);
                    
                    // 检查是否有$type字段
                    if (!json.Contains("\"$type\""))
                    {
                        var relativePath = jsonFile.Substring(modPath.Length + 1);
                        result.Warnings.Add($"Content file missing $type field: {relativePath}");
                    }
                    
                    // 尝试解析JSON
                    // 注意：这里只做基本语法检查
                    JsonUtility.FromJson<object>(json);
                }
                catch (Exception ex)
                {
                    var relativePath = jsonFile.Substring(modPath.Length + 1);
                    result.Errors.Add($"Invalid JSON in {relativePath}: {ex.Message}");
                }
            }
        }
    }
    
    public class ModValidationResult
    {
        public string Path;
        public string ModName;
        public List<string> Errors = new();
        public List<string> Warnings = new();
        
        public bool IsValid => Errors.Count == 0;
    }
}
#endif
