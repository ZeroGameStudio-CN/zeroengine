#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.ModSystem.Editor
{
    /// <summary>
    /// Mod创建向导窗口
    /// </summary>
    public class ModCreatorWindow : EditorWindow
    {
        private string _modId = "mycompany.mymod";
        private string _modName = "My Awesome Mod";
        private string _version = "1.0.0";
        private string _author = "";
        private string _description = "";
        private string _outputPath = "";
        
        private bool _createExampleAbility = true;
        private bool _createExampleBuff = false;
        
        private Vector2 _scrollPos;
        
        [MenuItem("ZeroEngine/Mod System/Create New Mod...", priority = 100)]
        public static void ShowWindow()
        {
            var window = GetWindow<ModCreatorWindow>("Create New Mod");
            window.minSize = new Vector2(450, 400);
            window.Show();
        }
        
        private void OnEnable()
        {
            // 默认输出到项目根目录/Mods
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? "";
            _outputPath = Path.Combine(projectRoot, "Mods");
        }
        
        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            
            EditorGUILayout.LabelField("Create New Mod", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);
            
            // 基本信息
            EditorGUILayout.LabelField("Basic Information", EditorStyles.boldLabel);
            _modId = EditorGUILayout.TextField("Mod ID", _modId);
            EditorGUILayout.HelpBox("Unique ID in format: author.modname (lowercase, no spaces)", MessageType.Info);
            
            _modName = EditorGUILayout.TextField("Display Name", _modName);
            _version = EditorGUILayout.TextField("Version", _version);
            _author = EditorGUILayout.TextField("Author", _author);
            
            EditorGUILayout.LabelField("Description");
            _description = EditorGUILayout.TextArea(_description, GUILayout.Height(60));
            
            EditorGUILayout.Space(10);
            
            // 输出路径
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _outputPath = EditorGUILayout.TextField("Output Folder", _outputPath);
            if (GUILayout.Button("Browse...", GUILayout.Width(80)))
            {
                var path = EditorUtility.OpenFolderPanel("Select Mods Folder", _outputPath, "");
                if (!string.IsNullOrEmpty(path))
                {
                    _outputPath = path;
                }
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(10);
            
            // 示例内容
            EditorGUILayout.LabelField("Example Content", EditorStyles.boldLabel);
            _createExampleAbility = EditorGUILayout.Toggle("Create Example Ability", _createExampleAbility);
            _createExampleBuff = EditorGUILayout.Toggle("Create Example Buff", _createExampleBuff);
            
            EditorGUILayout.Space(20);
            
            // 预览
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            var modFolder = Path.Combine(_outputPath, SanitizeFolderName(_modName));
            EditorGUILayout.HelpBox($"Mod will be created at:\n{modFolder}", MessageType.None);
            
            EditorGUILayout.Space(20);
            
            // 创建按钮
            GUI.enabled = ValidateInput();
            if (GUILayout.Button("Create Mod", GUILayout.Height(35)))
            {
                CreateMod(modFolder);
            }
            GUI.enabled = true;
            
            EditorGUILayout.EndScrollView();
        }
        
        private bool ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(_modId)) return false;
            if (string.IsNullOrWhiteSpace(_modName)) return false;
            if (string.IsNullOrWhiteSpace(_version)) return false;
            if (string.IsNullOrWhiteSpace(_outputPath)) return false;
            return true;
        }
        
        private string SanitizeFolderName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.Replace(' ', '_');
        }
        
        private void CreateMod(string modFolder)
        {
            try
            {
                // 创建目录结构
                Directory.CreateDirectory(modFolder);
                Directory.CreateDirectory(Path.Combine(modFolder, "content"));
                Directory.CreateDirectory(Path.Combine(modFolder, "assets"));
                
                // 创建manifest.json
                var manifest = new ModManifest
                {
                    Id = _modId,
                    Name = _modName,
                    Version = _version,
                    Author = _author,
                    Description = _description,
                    ContentPaths = new[] { "content" },
                    Enabled = true
                };
                
                var manifestJson = JsonUtility.ToJson(manifest, true);
                File.WriteAllText(Path.Combine(modFolder, "manifest.json"), manifestJson);
                
                // 创建示例内容
                if (_createExampleAbility)
                {
                    CreateExampleAbility(Path.Combine(modFolder, "content"));
                }
                
                if (_createExampleBuff)
                {
                    CreateExampleBuff(Path.Combine(modFolder, "content"));
                }
                
                // 创建README
                var readme = $@"# {_modName}

**Author:** {_author}
**Version:** {_version}

## Description

{_description}

## Installation

Copy this folder to the game's `Mods` directory.
";
                File.WriteAllText(Path.Combine(modFolder, "README.md"), readme);
                
                EditorUtility.DisplayDialog("Success", $"Mod created successfully!\n\n{modFolder}", "OK");
                
                // 在文件浏览器中打开
                EditorUtility.RevealInFinder(modFolder);
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to create mod:\n{ex.Message}", "OK");
            }
        }
        
        private void CreateExampleAbility(string contentFolder)
        {
            var ability = @"{
    ""$type"": ""AbilityDataSO"",
    ""AbilityName"": ""Example Ability"",
    ""Description"": ""An example ability from mod"",
    ""Icon"": ""assets/ability_icon.png"",
    ""Triggers"": [
        {
            ""$type"": ""ManualTriggerData"",
            ""ButtonName"": ""Fire1"",
            ""TriggerMultipleTimes"": false
        }
    ],
    ""Effects"": [
        {
            ""$type"": ""DamageEffectData"",
            ""TargetType"": ""Target"",
            ""DamageAmount"": 25,
            ""DamageType"": ""Physical""
        }
    ]
}";
            File.WriteAllText(Path.Combine(contentFolder, "example_ability.json"), ability);
        }
        
        private void CreateExampleBuff(string contentFolder)
        {
            var buff = @"{
    ""$type"": ""BuffData"",
    ""BuffId"": ""mod_example_buff"",
    ""Category"": ""Positive"",
    ""Icon"": ""assets/buff_icon.png"",
    ""Duration"": 10,
    ""MaxStacks"": 3,
    ""TickInterval"": 1,
    ""RefreshOnAddStack"": true,
    ""StatModifiers"": [
        {
            ""StatId"": ""offense.attack"",
            ""Value"": 10,
            ""ModType"": ""Flat""
        }
    ]
}";
            File.WriteAllText(Path.Combine(contentFolder, "example_buff.json"), buff);
        }
    }
}
#endif
