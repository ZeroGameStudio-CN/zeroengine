using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Core;
using ZeroEngine.Save;

namespace ZeroEngine.Settings
{
    /// <summary>
    /// 游戏设置管理器
    /// </summary>
    [Obsolete("ZeroEngine.Settings is retained for compatibility. Use com.zerogamestudio.zeroengine.settings / ZeroEngine.PlayerSettings.", false)]
    public class SettingsManager : MonoSingleton<SettingsManager>, ISaveable
    {
        [Header("设置定义")]
        [SerializeField] private SettingsDefinitionSO _settingsDefinition;

        [Header("按键绑定")]
        [SerializeField] private List<KeyBindingData> _defaultKeyBindings = new List<KeyBindingData>();

        [Header("调试")]
        [SerializeField] private bool _debugMode;

        // 当前设置值
        private readonly Dictionary<string, SettingValue> _values = new Dictionary<string, SettingValue>();
        private readonly Dictionary<string, SettingValue> _pendingValues = new Dictionary<string, SettingValue>();

        // 按键绑定
        private readonly Dictionary<string, KeyBindingData> _keyBindings = new Dictionary<string, KeyBindingData>();

        // 定义缓存
        private readonly Dictionary<string, SettingDefinition> _definitions = new Dictionary<string, SettingDefinition>();
        private readonly Dictionary<SettingCategory, List<SettingDefinition>> _categoryCache = new Dictionary<SettingCategory, List<SettingDefinition>>();

        // 变更追踪
        private bool _hasUnappliedChanges;

        #region Events

        public event Action<SettingsEventArgs> OnSettingsEvent;

        #endregion

        #region Properties

        public bool HasUnappliedChanges => _hasUnappliedChanges;

        #endregion

        #region ISaveable

        public string SaveKey => "SettingsManager";

        public void Register() => SaveSlotManager.Instance?.Register(this);
        public void Unregister() => SaveSlotManager.Instance?.Unregister(this);

        public object ExportSaveData()
        {
            var keyBindingsList = new List<KeyBindingData>();
            foreach (var kvp in _keyBindings)
                keyBindingsList.Add(kvp.Value);

            return new SettingsSaveData
            {
                Values = new Dictionary<string, SettingValue>(_values),
                KeyBindings = keyBindingsList
            };
        }

        public void ImportSaveData(object data)
        {
            if (data is not SettingsSaveData saveData) return;

            _values.Clear();
            if (saveData.Values != null)
            {
                foreach (var kvp in saveData.Values)
                    _values[kvp.Key] = kvp.Value;
            }

            _keyBindings.Clear();
            if (saveData.KeyBindings != null)
            {
                foreach (var binding in saveData.KeyBindings)
                    _keyBindings[binding.ActionId] = binding;
            }

            ApplyAllSettings();
            OnSettingsEvent?.Invoke(SettingsEventArgs.Loaded());
        }

        public void ResetToDefault()
        {
            _values.Clear();
            _keyBindings.Clear();

            // 加载默认值
            if (_settingsDefinition != null)
            {
                foreach (var def in _settingsDefinition.Settings)
                {
                    _values[def.Key] = new SettingValue { Key = def.Key, StringValue = def.DefaultValue };
                }
            }

            foreach (var binding in _defaultKeyBindings)
            {
                _keyBindings[binding.ActionId] = binding;
            }

            ApplyAllSettings();
        }

        #endregion

        #region Unity Lifecycle

        protected override void Awake()
        {
            base.Awake();
            BuildDefinitionCache();
            LoadDefaults();
        }

        private void Start()
        {
            Register();
        }

        protected override void OnDestroy()
        {
            Unregister();
            base.OnDestroy();
        }

        #endregion

        #region Public API - Get/Set

        /// <summary>获取布尔设置</summary>
        public bool GetBool(string key, bool defaultValue = false)
        {
            return _values.TryGetValue(key, out var value) ? value.GetBool() : defaultValue;
        }

        /// <summary>获取整数设置</summary>
        public int GetInt(string key, int defaultValue = 0)
        {
            return _values.TryGetValue(key, out var value) ? value.GetInt() : defaultValue;
        }

        /// <summary>获取浮点设置</summary>
        public float GetFloat(string key, float defaultValue = 0f)
        {
            return _values.TryGetValue(key, out var value) ? value.GetFloat() : defaultValue;
        }

        /// <summary>获取字符串设置</summary>
        public string GetString(string key, string defaultValue = "")
        {
            return _values.TryGetValue(key, out var value) ? value.StringValue : defaultValue;
        }

        /// <summary>设置布尔值</summary>
        public void SetBool(string key, bool value, bool immediate = false)
        {
            SetValue(key, value ? "true" : "false", immediate);
        }

        /// <summary>设置整数值</summary>
        public void SetInt(string key, int value, bool immediate = false)
        {
            SetValue(key, value.ToString(), immediate);
        }

        /// <summary>设置浮点值</summary>
        public void SetFloat(string key, float value, bool immediate = false)
        {
            SetValue(key, value.ToString("F2"), immediate);
        }

        /// <summary>设置字符串值</summary>
        public void SetString(string key, string value, bool immediate = false)
        {
            SetValue(key, value, immediate);
        }

        private void SetValue(string key, string newValue, bool immediate)
        {
            string oldValue = _values.TryGetValue(key, out var v) ? v.StringValue : "";

            if (oldValue == newValue) return;

            var settingValue = new SettingValue { Key = key, StringValue = newValue };

            if (immediate)
            {
                _values[key] = settingValue;
                ApplySetting(key);
            }
            else
            {
                _pendingValues[key] = settingValue;
                _hasUnappliedChanges = true;
            }

            var category = _definitions.TryGetValue(key, out var def) ? def.Category : SettingCategory.General;
            OnSettingsEvent?.Invoke(SettingsEventArgs.ValueChanged(key, oldValue, newValue, category));
            Log($"设置变更: {key} = {newValue}");
        }

        #endregion

        #region Public API - Apply/Reset

        /// <summary>应用所有待定更改</summary>
        public void ApplyChanges()
        {
            foreach (var kvp in _pendingValues)
            {
                _values[kvp.Key] = kvp.Value;
                ApplySetting(kvp.Key);
            }

            _pendingValues.Clear();
            _hasUnappliedChanges = false;

            OnSettingsEvent?.Invoke(SettingsEventArgs.Applied());
            Log("设置已应用");
        }

        /// <summary>取消待定更改</summary>
        public void DiscardChanges()
        {
            _pendingValues.Clear();
            _hasUnappliedChanges = false;
            Log("设置更改已取消");
        }

        /// <summary>重置为默认值</summary>
        public void ResetCategory(SettingCategory category)
        {
            if (_settingsDefinition == null) return;

            foreach (var def in _settingsDefinition.Settings)
            {
                if (def.Category == category)
                {
                    _values[def.Key] = new SettingValue { Key = def.Key, StringValue = def.DefaultValue };
                    ApplySetting(def.Key);
                }
            }

            OnSettingsEvent?.Invoke(SettingsEventArgs.Reset(category));
            Log($"重置分类: {category}");
        }

        /// <summary>重置所有设置</summary>
        public void ResetAll()
        {
            ResetToDefault();
            OnSettingsEvent?.Invoke(SettingsEventArgs.Reset());
            Log("重置所有设置");
        }

        /// <summary>应用图形预设</summary>
        public void ApplyGraphicsPreset(string presetName)
        {
            if (GraphicsPresets.Presets.TryGetValue(presetName, out var preset))
            {
                foreach (var kvp in preset)
                {
                    SetValue(kvp.Key, kvp.Value, true);
                }
                Log($"应用图形预设: {presetName}");
            }
        }

        #endregion

        #region Public API - Key Bindings

        /// <summary>获取按键绑定</summary>
        public KeyBindingData GetKeyBinding(string actionId)
        {
            _keyBindings.TryGetValue(actionId, out var binding);
            return binding;
        }

        /// <summary>设置按键绑定</summary>
        public void SetKeyBinding(string actionId, KeyCode primaryKey, KeyCode secondaryKey = KeyCode.None, KeyCode modifier = KeyCode.None)
        {
            if (!_keyBindings.TryGetValue(actionId, out var binding))
            {
                binding = new KeyBindingData { ActionId = actionId };
                _keyBindings[actionId] = binding;
            }

            binding.PrimaryKey = primaryKey;
            binding.SecondaryKey = secondaryKey;
            binding.Modifier = modifier;

            Log($"按键绑定更新: {actionId} = {binding.GetDisplayString()}");
        }

        /// <summary>重置按键绑定</summary>
        public void ResetKeyBindings()
        {
            _keyBindings.Clear();
            foreach (var binding in _defaultKeyBindings)
            {
                _keyBindings[binding.ActionId] = new KeyBindingData
                {
                    ActionId = binding.ActionId,
                    DisplayName = binding.DisplayName,
                    PrimaryKey = binding.PrimaryKey,
                    SecondaryKey = binding.SecondaryKey,
                    Modifier = binding.Modifier
                };
            }
            Log("按键绑定已重置");
        }

        /// <summary>检查按键是否按下</summary>
        public bool IsActionPressed(string actionId)
        {
            return _keyBindings.TryGetValue(actionId, out var binding) && binding.IsPressed();
        }

        /// <summary>检查按键是否按住</summary>
        public bool IsActionHeld(string actionId)
        {
            return _keyBindings.TryGetValue(actionId, out var binding) && binding.IsHeld();
        }

        /// <summary>获取所有按键绑定</summary>
        public IReadOnlyDictionary<string, KeyBindingData> GetAllKeyBindings() => _keyBindings;

        #endregion

        #region Public API - Query

        /// <summary>获取设置定义</summary>
        public SettingDefinition GetDefinition(string key)
        {
            _definitions.TryGetValue(key, out var def);
            return def;
        }

        /// <summary>获取分类下的设置</summary>
        public void GetSettingsByCategory(SettingCategory category, List<SettingDefinition> results)
        {
            results.Clear();
            if (_categoryCache.TryGetValue(category, out var list))
            {
                results.AddRange(list);
            }
        }

        #endregion

        #region Internal

        private void BuildDefinitionCache()
        {
            _definitions.Clear();
            _categoryCache.Clear();

            if (_settingsDefinition == null) return;

            foreach (var def in _settingsDefinition.Settings)
            {
                _definitions[def.Key] = def;

                if (!_categoryCache.TryGetValue(def.Category, out var list))
                {
                    list = new List<SettingDefinition>();
                    _categoryCache[def.Category] = list;
                }
                list.Add(def);
            }

            // 排序
            foreach (var list in _categoryCache.Values)
            {
                list.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
            }
        }

        private void LoadDefaults()
        {
            if (_settingsDefinition != null)
            {
                foreach (var def in _settingsDefinition.Settings)
                {
                    if (!_values.ContainsKey(def.Key))
                    {
                        _values[def.Key] = new SettingValue { Key = def.Key, StringValue = def.DefaultValue };
                    }
                }
            }

            foreach (var binding in _defaultKeyBindings)
            {
                if (!_keyBindings.ContainsKey(binding.ActionId))
                {
                    _keyBindings[binding.ActionId] = binding;
                }
            }
        }

        private void ApplyAllSettings()
        {
            foreach (var key in _values.Keys)
            {
                ApplySetting(key);
            }
        }

        private void ApplySetting(string key)
        {
            if (!_values.TryGetValue(key, out var value)) return;

            // 应用常见设置
            switch (key)
            {
                case "master_volume":
                    AudioListener.volume = value.GetFloat();
                    break;
                case "vsync":
                    QualitySettings.vSyncCount = value.GetBool() ? 1 : 0;
                    break;
                case "target_framerate":
                    Application.targetFrameRate = value.GetInt();
                    break;
                case "graphics_quality":
                    QualitySettings.SetQualityLevel(value.GetInt());
                    break;
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("ZEROENGINE_DEBUG")]
        private void Log(string message)
        {
            if (_debugMode) Debug.Log($"[Settings] {message}");
        }

        #endregion
    }

    #region ScriptableObjects

    [CreateAssetMenu(fileName = "Settings Definition", menuName = "ZeroEngine/Settings/Settings Definition")]
    [Obsolete("ZeroEngine.Settings is retained for compatibility. Use ZeroEngine.PlayerSettings settings catalog.", false)]
    public class SettingsDefinitionSO : ScriptableObject
    {
        public List<SettingDefinition> Settings = new List<SettingDefinition>();
    }

    #endregion

    #region Save Data

    [Serializable]
    [Obsolete("ZeroEngine.Settings is retained for compatibility. Use ZeroEngine.PlayerSettings settings documents.", false)]
    public class SettingsSaveData
    {
        public Dictionary<string, SettingValue> Values;
        public List<KeyBindingData> KeyBindings;
    }

    #endregion
}
