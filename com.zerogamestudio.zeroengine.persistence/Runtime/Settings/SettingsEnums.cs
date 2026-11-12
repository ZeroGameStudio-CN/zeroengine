using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Settings
{
    /// <summary>设置分类</summary>
    [Obsolete("ZeroEngine.Settings is retained for compatibility. Use ZeroEngine.PlayerSettings.", false)]
    public enum SettingCategory
    {
        General,        // 通用
        Graphics,       // 画质
        Audio,          // 音频
        Controls,       // 控制
        Gameplay,       // 游戏性
        Accessibility,  // 无障碍
        Language,       // 语言
        Network,        // 网络
        Advanced        // 高级
    }

    /// <summary>设置值类型</summary>
    [Obsolete("ZeroEngine.Settings is retained for compatibility. Use ZeroEngine.PlayerSettings.", false)]
    public enum SettingValueType
    {
        Bool,           // 开关
        Int,            // 整数
        Float,          // 浮点
        String,         // 字符串
        Enum,           // 枚举选项
        KeyBinding,     // 按键绑定
        Resolution,     // 分辨率
        Slider,         // 滑动条
        Color           // 颜色
    }

    /// <summary>设置事件类型</summary>
    [Obsolete("ZeroEngine.Settings is retained for compatibility. Use ZeroEngine.PlayerSettings.", false)]
    public enum SettingsEventType
    {
        ValueChanged,
        Applied,
        Reset,
        Saved,
        Loaded
    }

    /// <summary>设置项定义</summary>
    [Serializable]
    [Obsolete("ZeroEngine.Settings is retained for compatibility. Use ZeroEngine.PlayerSettings.", false)]
    public class SettingDefinition
    {
        [Header("基础")]
        public string Key;
        public string DisplayName;
        [TextArea] public string Description;
        public SettingCategory Category = SettingCategory.General;

        [Header("值")]
        public SettingValueType ValueType = SettingValueType.Bool;
        public string DefaultValue;

        [Header("约束 (数值类型)")]
        public float MinValue;
        public float MaxValue = 100;
        public float Step = 1;

        [Header("选项 (枚举类型)")]
        public List<SettingOption> Options = new List<SettingOption>();

        [Header("显示")]
        public int SortOrder;
        public bool RequiresRestart;
        public bool Hidden;

        [Header("联动")]
        public string DependsOnKey;
        public string DependsOnValue;
    }

    /// <summary>设置选项 (用于枚举类型)</summary>
    [Serializable]
    [Obsolete("ZeroEngine.Settings is retained for compatibility. Use ZeroEngine.PlayerSettings.", false)]
    public class SettingOption
    {
        public string Value;
        public string DisplayName;
        public string Description;
        public Sprite Icon;
    }

    /// <summary>按键绑定</summary>
    [Serializable]
    [Obsolete("ZeroEngine.Settings is retained for compatibility. Use ZeroEngine.PlayerSettings.", false)]
    public class KeyBindingData
    {
        public string ActionId;
        public string DisplayName;
        public KeyCode PrimaryKey = KeyCode.None;
        public KeyCode SecondaryKey = KeyCode.None;
        public KeyCode Modifier = KeyCode.None;

        public bool IsPressed()
        {
            if (Modifier != KeyCode.None && !Input.GetKey(Modifier))
                return false;

            return Input.GetKeyDown(PrimaryKey) ||
                   (SecondaryKey != KeyCode.None && Input.GetKeyDown(SecondaryKey));
        }

        public bool IsHeld()
        {
            if (Modifier != KeyCode.None && !Input.GetKey(Modifier))
                return false;

            return Input.GetKey(PrimaryKey) ||
                   (SecondaryKey != KeyCode.None && Input.GetKey(SecondaryKey));
        }

        public string GetDisplayString()
        {
            string result = "";
            if (Modifier != KeyCode.None)
                result = Modifier.ToString() + "+";
            result += PrimaryKey.ToString();
            if (SecondaryKey != KeyCode.None)
                result += " / " + SecondaryKey.ToString();
            return result;
        }
    }

    /// <summary>分辨率数据</summary>
    [Serializable]
    [Obsolete("ZeroEngine.Settings is retained for compatibility. Use ZeroEngine.PlayerSettings.", false)]
    public class ResolutionData
    {
        public int Width;
        public int Height;
        public int RefreshRate;
        public bool Fullscreen;

        public ResolutionData() { }

        public ResolutionData(int width, int height, int refreshRate = 60, bool fullscreen = true)
        {
            Width = width;
            Height = height;
            RefreshRate = refreshRate;
            Fullscreen = fullscreen;
        }

        public override string ToString() => $"{Width}x{Height}@{RefreshRate}Hz";

        public void Apply()
        {
            var rate = new UnityEngine.RefreshRate { numerator = (uint)RefreshRate, denominator = 1 };
            Screen.SetResolution(Width, Height, Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed, rate);
        }
    }

    /// <summary>设置值包装</summary>
    [Serializable]
    [Obsolete("ZeroEngine.Settings is retained for compatibility. Use ZeroEngine.PlayerSettings.", false)]
    public class SettingValue
    {
        public string Key;
        public string StringValue;

        public bool GetBool() => StringValue == "true" || StringValue == "1";
        public int GetInt() => int.TryParse(StringValue, out int v) ? v : 0;
        public float GetFloat() => float.TryParse(StringValue, out float v) ? v : 0f;

        public void SetBool(bool value) => StringValue = value ? "true" : "false";
        public void SetInt(int value) => StringValue = value.ToString();
        public void SetFloat(float value) => StringValue = value.ToString("F2");
        public void SetString(string value) => StringValue = value;
    }

    /// <summary>设置事件参数</summary>
    [Obsolete("ZeroEngine.Settings is retained for compatibility. Use ZeroEngine.PlayerSettings.", false)]
    public class SettingsEventArgs
    {
        public SettingsEventType Type { get; private set; }
        public string Key { get; private set; }
        public string OldValue { get; private set; }
        public string NewValue { get; private set; }
        public SettingCategory Category { get; private set; }

        public static SettingsEventArgs ValueChanged(string key, string oldValue, string newValue, SettingCategory category)
            => new() { Type = SettingsEventType.ValueChanged, Key = key, OldValue = oldValue, NewValue = newValue, Category = category };

        public static SettingsEventArgs Applied()
            => new() { Type = SettingsEventType.Applied };

        public static SettingsEventArgs Reset(SettingCategory? category = null)
            => new() { Type = SettingsEventType.Reset, Category = category ?? SettingCategory.General };

        public static SettingsEventArgs Saved()
            => new() { Type = SettingsEventType.Saved };

        public static SettingsEventArgs Loaded()
            => new() { Type = SettingsEventType.Loaded };
    }

    /// <summary>预设图形设置</summary>
    [Obsolete("ZeroEngine.Settings is retained for compatibility. Use ZeroEngine.PlayerSettings.", false)]
    public static class GraphicsPresets
    {
        public static readonly Dictionary<string, Dictionary<string, string>> Presets = new()
        {
            ["Low"] = new Dictionary<string, string>
            {
                ["graphics_quality"] = "0",
                ["shadow_quality"] = "0",
                ["texture_quality"] = "0",
                ["antialiasing"] = "0",
                ["vsync"] = "0"
            },
            ["Medium"] = new Dictionary<string, string>
            {
                ["graphics_quality"] = "1",
                ["shadow_quality"] = "1",
                ["texture_quality"] = "1",
                ["antialiasing"] = "1",
                ["vsync"] = "1"
            },
            ["High"] = new Dictionary<string, string>
            {
                ["graphics_quality"] = "2",
                ["shadow_quality"] = "2",
                ["texture_quality"] = "2",
                ["antialiasing"] = "2",
                ["vsync"] = "1"
            },
            ["Ultra"] = new Dictionary<string, string>
            {
                ["graphics_quality"] = "3",
                ["shadow_quality"] = "3",
                ["texture_quality"] = "3",
                ["antialiasing"] = "3",
                ["vsync"] = "1"
            }
        };
    }
}
