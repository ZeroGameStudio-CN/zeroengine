using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ZeroEngine.PlayerSettings.UI
{
    public enum StandardSettingsUiCategory
    {
        Controls,
        Display,
        Audio,
        Accessibility
    }

    public sealed class StandardSettingsUiProfile
    {
        public static StandardSettingsUiProfile Default { get; } = new();

        public Vector2 PointerSensitivityRange { get; } = new(0.25f, 2f);
        public Vector2 GamepadSensitivityRange { get; } = new(0.25f, 2f);
        public Vector2 GamepadDeadzoneRange { get; } = new(0f, 0.5f);
        public Vector2 VibrationRange { get; } = new(0f, 1f);
        public Vector2 VolumeRange { get; } = new(0f, 1f);
        public Vector2 UiScaleRange { get; } = new(0.75f, 1.5f);
        public Vector2Int FrameRateRange { get; } = new(30, 240);
    }

    public sealed class StandardSettingsUiText
    {
        public string Title { get; set; } = "Settings";
        public string Subtitle { get; set; } = "Changes preview immediately and save on return";
        public string ControlsTab { get; set; } = "Controls";
        public string DisplayTab { get; set; } = "Display";
        public string AudioTab { get; set; } = "Audio";
        public string AccessibilityTab { get; set; } = "Accessibility";
        public string PointerSensitivity { get; set; } = "Mouse Sensitivity";
        public string GamepadSensitivity { get; set; } = "Gamepad Sensitivity";
        public string GamepadDeadzone { get; set; } = "Stick Deadzone";
        public string InvertY { get; set; } = "Invert Y Axis";
        public string Vibration { get; set; } = "Gamepad Vibration";
        public string GlyphStyle { get; set; } = "Gamepad Glyphs";
        public string Bindings { get; set; } = "Bindings";
        public string OpenBindings { get; set; } = "Open";
        public string WindowMode { get; set; } = "Window Mode";
        public string Resolution { get; set; } = "Resolution";
        public string RefreshRate { get; set; } = "Refresh Rate";
        public string VerticalSync { get; set; } = "Vertical Sync";
        public string FrameRateLimit { get; set; } = "Frame Rate Limit";
        public string Quality { get; set; } = "Quality";
        public string MasterVolume { get; set; } = "Master Volume";
        public string MusicVolume { get; set; } = "Music Volume";
        public string SfxVolume { get; set; } = "Sound Effects Volume";
        public string UiScale { get; set; } = "UI Scale";
        public string HighContrast { get; set; } = "High Contrast UI";
        public string ReduceMotion { get; set; } = "Reduce UI Motion";
        public string Language { get; set; } = "Language";
        public string RestoreDefaults { get; set; } = "Restore Category Defaults";
        public string SaveAndBack { get; set; } = "Save and Back";

        public static StandardSettingsUiText English => new();

        public static StandardSettingsUiText SimplifiedChinese => new()
        {
            Title = "设置",
            Subtitle = "修改会即时预览，返回时自动保存",
            ControlsTab = "操作",
            DisplayTab = "显示",
            AudioTab = "声音",
            AccessibilityTab = "辅助",
            PointerSensitivity = "鼠标灵敏度",
            GamepadSensitivity = "手柄灵敏度",
            GamepadDeadzone = "摇杆死区",
            InvertY = "反转镜头 Y 轴",
            Vibration = "手柄震动",
            GlyphStyle = "手柄提示",
            Bindings = "按键绑定",
            OpenBindings = "重新绑定操作",
            WindowMode = "窗口模式",
            Resolution = "分辨率",
            RefreshRate = "刷新率",
            VerticalSync = "垂直同步",
            FrameRateLimit = "帧率上限",
            Quality = "画质",
            MasterVolume = "主音量",
            MusicVolume = "音乐音量",
            SfxVolume = "音效音量",
            UiScale = "界面缩放",
            HighContrast = "高对比界面",
            ReduceMotion = "减少界面动效",
            Language = "语言",
            RestoreDefaults = "恢复本类默认值",
            SaveAndBack = "保存并返回"
        };
    }

    public sealed class StandardSettingsUiView
    {
        private static readonly StandardSettingsUiCategory[] CategoryOrder =
        {
            StandardSettingsUiCategory.Controls,
            StandardSettingsUiCategory.Display,
            StandardSettingsUiCategory.Audio,
            StandardSettingsUiCategory.Accessibility
        };

        private readonly Dictionary<StandardSettingsUiCategory, Button> _tabs;
        private readonly Dictionary<StandardSettingsUiCategory, SettingsUiCategoryView> _categories;
        private readonly Dictionary<SettingId, Selectable> _controls;
        private readonly Dictionary<SettingId, Text> _values;
        private readonly Dictionary<SettingId, Text> _labels;
        private readonly Text _title;
        private readonly Text _subtitle;
        private StandardSettingsUiText _text;

        internal StandardSettingsUiView(
            SettingsUiLayoutBuilder layout,
            SettingsUiShell shell,
            Dictionary<StandardSettingsUiCategory, Button> tabs,
            Dictionary<StandardSettingsUiCategory, SettingsUiCategoryView> categories,
            Dictionary<SettingId, Selectable> controls,
            Dictionary<SettingId, Text> values,
            Dictionary<SettingId, Text> labels,
            Text title,
            Text subtitle,
            Button resetButton,
            Button saveButton)
        {
            Layout = layout;
            Shell = shell;
            _tabs = tabs;
            _categories = categories;
            _controls = controls;
            _values = values;
            _labels = labels;
            _title = title;
            _subtitle = subtitle;
            ResetButton = resetButton;
            SaveButton = saveButton;
        }

        public event Action<StandardSettingsUiCategory> CategoryChanged;

        public SettingsUiLayoutBuilder Layout { get; }
        public SettingsUiShell Shell { get; }
        public Button ResetButton { get; }
        public Button SaveButton { get; }
        public StandardSettingsUiCategory SelectedCategory { get; private set; }
        public IReadOnlyDictionary<SettingId, Selectable> Controls => _controls;

        public SettingsUiCategoryView Category(StandardSettingsUiCategory category) =>
            _categories[category];

        public Button Tab(StandardSettingsUiCategory category) => _tabs[category];

        public Slider Slider(SettingId id) => Control<Slider>(id);

        public Toggle Toggle(SettingId id) => Control<Toggle>(id);

        public Button Choice(SettingId id) => Control<Button>(id);

        public Text ValueText(SettingId id)
        {
            if (!_values.TryGetValue(id, out Text value))
            {
                throw new ArgumentException($"Setting '{id}' does not have value text.", nameof(id));
            }

            return value;
        }

        public void ApplyText(StandardSettingsUiText text)
        {
            _text = text ?? throw new ArgumentNullException(nameof(text));
            _title.text = text.Title;
            _subtitle.text = text.Subtitle;
            SetText(_tabs[StandardSettingsUiCategory.Controls], TabLabel(StandardSettingsUiCategory.Controls));
            SetText(_tabs[StandardSettingsUiCategory.Display], TabLabel(StandardSettingsUiCategory.Display));
            SetText(_tabs[StandardSettingsUiCategory.Audio], TabLabel(StandardSettingsUiCategory.Audio));
            SetText(_tabs[StandardSettingsUiCategory.Accessibility], TabLabel(StandardSettingsUiCategory.Accessibility));

            SetLabel(StandardSettingIds.PointerSensitivity, text.PointerSensitivity);
            SetLabel(StandardSettingIds.GamepadSensitivity, text.GamepadSensitivity);
            SetLabel(StandardSettingIds.GamepadDeadzone, text.GamepadDeadzone);
            SetLabel(StandardSettingIds.InvertY, text.InvertY);
            SetLabel(StandardSettingIds.Vibration, text.Vibration);
            SetLabel(StandardSettingIds.GlyphStyle, text.GlyphStyle);
            SetLabel(StandardSettingIds.BindingOverrides, text.Bindings);
            SetText(Choice(StandardSettingIds.BindingOverrides), text.OpenBindings);
            SetLabel(StandardSettingIds.WindowMode, text.WindowMode);
            SetLabel(StandardSettingIds.Width, text.Resolution);
            SetLabel(StandardSettingIds.RefreshRate, text.RefreshRate);
            SetLabel(StandardSettingIds.VSyncCount, text.VerticalSync);
            SetLabel(StandardSettingIds.FrameRateLimit, text.FrameRateLimit);
            SetLabel(StandardSettingIds.Quality, text.Quality);
            SetLabel(StandardSettingIds.MasterVolume, text.MasterVolume);
            SetLabel(StandardSettingIds.MusicVolume, text.MusicVolume);
            SetLabel(StandardSettingIds.SfxVolume, text.SfxVolume);
            SetLabel(StandardSettingIds.UiScale, text.UiScale);
            SetLabel(StandardSettingIds.HighContrast, text.HighContrast);
            SetLabel(StandardSettingIds.ReduceMotion, text.ReduceMotion);
            SetLabel(StandardSettingIds.Locale, text.Language);
            SetText(ResetButton, text.RestoreDefaults);
            SetText(SaveButton, text.SaveAndBack);
            UpdateTabLabels();
        }

        public void ShowCategory(
            StandardSettingsUiCategory category,
            bool selectFirst = false)
        {
            SelectedCategory = category;
            foreach (StandardSettingsUiCategory candidate in CategoryOrder)
            {
                _categories[candidate].Root.SetActive(candidate == category);
            }

            UpdateTabLabels();
            RefreshNavigation();
            if (selectFirst
                && _categories[category].Selectables.Count > 0
                && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(
                    _categories[category].Selectables[0].gameObject);
            }

            CategoryChanged?.Invoke(category);
        }

        public void Rebuild()
        {
            SettingsUiLayoutBuilder.Rebuild(
                Shell,
                _categories[StandardSettingsUiCategory.Controls],
                _categories[StandardSettingsUiCategory.Display],
                _categories[StandardSettingsUiCategory.Audio],
                _categories[StandardSettingsUiCategory.Accessibility]);
            RefreshNavigation();
        }

        public void RefreshNavigation()
        {
            for (var index = 0; index < CategoryOrder.Length; index++)
            {
                StandardSettingsUiCategory category = CategoryOrder[index];
                Button tab = _tabs[category];
                IReadOnlyList<Selectable> rows = _categories[category].Selectables;
                SetNavigation(
                    tab,
                    _tabs[CategoryOrder[(index + CategoryOrder.Length - 1) % CategoryOrder.Length]],
                    _tabs[CategoryOrder[(index + 1) % CategoryOrder.Length]],
                    SaveButton,
                    rows.Count > 0 ? rows[0] : ResetButton);

                for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    SetNavigation(
                        rows[rowIndex],
                        null,
                        null,
                        rowIndex == 0 ? tab : rows[rowIndex - 1],
                        rowIndex == rows.Count - 1 ? ResetButton : rows[rowIndex + 1]);
                }
            }

            IReadOnlyList<Selectable> selectedRows = _categories[SelectedCategory].Selectables;
            Selectable footerUp = selectedRows.Count == 0
                ? _tabs[SelectedCategory]
                : selectedRows[selectedRows.Count - 1];
            SetNavigation(ResetButton, null, SaveButton, footerUp, _tabs[SelectedCategory]);
            SetNavigation(SaveButton, ResetButton, null, footerUp, _tabs[SelectedCategory]);
        }

        private T Control<T>(SettingId id) where T : Selectable
        {
            if (!_controls.TryGetValue(id, out Selectable selectable)
                || selectable is not T typed)
            {
                throw new ArgumentException(
                    $"Setting '{id}' is not represented by {typeof(T).Name}.",
                    nameof(id));
            }

            return typed;
        }

        private void SetLabel(SettingId id, string value)
        {
            if (_labels.TryGetValue(id, out Text label))
            {
                label.text = value ?? string.Empty;
            }
        }

        private string TabLabel(StandardSettingsUiCategory category)
        {
            if (_text == null)
            {
                return string.Empty;
            }

            return category switch
            {
                StandardSettingsUiCategory.Controls => _text.ControlsTab,
                StandardSettingsUiCategory.Display => _text.DisplayTab,
                StandardSettingsUiCategory.Audio => _text.AudioTab,
                _ => _text.AccessibilityTab
            };
        }

        private void UpdateTabLabels()
        {
            if (_text == null)
            {
                return;
            }

            foreach (StandardSettingsUiCategory category in CategoryOrder)
            {
                string label = TabLabel(category);
                SetText(_tabs[category], category == SelectedCategory ? $"◆ {label}" : label);
            }
        }

        private static void SetText(Button button, string value)
        {
            Text text = button.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.text = value ?? string.Empty;
            }
        }

        private static void SetNavigation(
            Selectable selectable,
            Selectable left,
            Selectable right,
            Selectable up,
            Selectable down)
        {
            Navigation navigation = selectable.navigation;
            navigation.mode = Navigation.Mode.Explicit;
            navigation.selectOnLeft = left;
            navigation.selectOnRight = right;
            navigation.selectOnUp = up;
            navigation.selectOnDown = down;
            selectable.navigation = navigation;
        }
    }

    public sealed class StandardSettingsUiBuilder
    {
        private readonly SettingsUiLayoutBuilder _layout;
        private readonly StandardSettingsUiProfile _profile;

        public StandardSettingsUiBuilder(
            RectTransform host,
            Font fallbackFont = null,
            SettingsUiTheme theme = null)
            : this(
                new SettingsUiLayoutBuilder(host, fallbackFont, theme),
                StandardSettingsUiProfile.Default)
        {
        }

        public StandardSettingsUiBuilder(
            RectTransform host,
            SettingsUiStyle style)
            : this(
                new SettingsUiLayoutBuilder(host, style),
                StandardSettingsUiProfile.Default)
        {
        }

        internal StandardSettingsUiBuilder(
            SettingsUiLayoutBuilder layout,
            StandardSettingsUiProfile profile)
        {
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        }

        public StandardSettingsUiView Build(StandardSettingsUiText text = null)
        {
            text ??= StandardSettingsUiText.English;
            SettingsUiShell shell = _layout.BuildShell(text.Title, text.Subtitle);
            var tabs = new Dictionary<StandardSettingsUiCategory, Button>
            {
                [StandardSettingsUiCategory.Controls] =
                    _layout.CreateTab(shell, "Controls Tab", text.ControlsTab),
                [StandardSettingsUiCategory.Display] =
                    _layout.CreateTab(shell, "Display Tab", text.DisplayTab),
                [StandardSettingsUiCategory.Audio] =
                    _layout.CreateTab(shell, "Audio Tab", text.AudioTab),
                [StandardSettingsUiCategory.Accessibility] =
                    _layout.CreateTab(shell, "Accessibility Tab", text.AccessibilityTab)
            };
            var categories = new Dictionary<StandardSettingsUiCategory, SettingsUiCategoryView>
            {
                [StandardSettingsUiCategory.Controls] =
                    _layout.CreateCategory(shell, "Controls Settings"),
                [StandardSettingsUiCategory.Display] =
                    _layout.CreateCategory(shell, "Display Settings"),
                [StandardSettingsUiCategory.Audio] =
                    _layout.CreateCategory(shell, "Audio Settings"),
                [StandardSettingsUiCategory.Accessibility] =
                    _layout.CreateCategory(shell, "Accessibility Settings")
            };
            var controls = new Dictionary<SettingId, Selectable>();
            var values = new Dictionary<SettingId, Text>();
            var labels = new Dictionary<SettingId, Text>();

            SettingsUiCategoryView controlsCategory =
                categories[StandardSettingsUiCategory.Controls];
            AddSlider(
                controlsCategory,
                "Pointer Sensitivity",
                StandardSettingIds.PointerSensitivity,
                text.PointerSensitivity,
                _profile.PointerSensitivityRange,
                controls,
                values,
                labels);
            AddSlider(
                controlsCategory,
                "Gamepad Sensitivity",
                StandardSettingIds.GamepadSensitivity,
                text.GamepadSensitivity,
                _profile.GamepadSensitivityRange,
                controls,
                values,
                labels);
            AddSlider(
                controlsCategory,
                "Gamepad Deadzone",
                StandardSettingIds.GamepadDeadzone,
                text.GamepadDeadzone,
                _profile.GamepadDeadzoneRange,
                controls,
                values,
                labels);
            AddToggle(
                controlsCategory,
                "Invert Y",
                StandardSettingIds.InvertY,
                text.InvertY,
                controls,
                labels);
            AddSlider(
                controlsCategory,
                "Gamepad Vibration",
                StandardSettingIds.Vibration,
                text.Vibration,
                _profile.VibrationRange,
                controls,
                values,
                labels);
            AddChoice(
                controlsCategory,
                "Gamepad Glyph Style",
                new[] { StandardSettingIds.GlyphStyle },
                text.GlyphStyle,
                controls,
                values,
                labels);
            Button rebind = _layout.CreateActionRow(
                controlsCategory,
                "Rebind Controls",
                text.Bindings,
                text.OpenBindings,
                true);
            Register(
                new[] { StandardSettingIds.BindingOverrides },
                rebind,
                rebind.GetComponentInChildren<Text>(),
                FindLabel(controlsCategory, "Rebind Controls"),
                controls,
                values,
                labels);

            SettingsUiCategoryView displayCategory =
                categories[StandardSettingsUiCategory.Display];
            AddChoice(
                displayCategory,
                "Window Mode",
                new[] { StandardSettingIds.WindowMode },
                text.WindowMode,
                controls,
                values,
                labels);
            AddChoice(
                displayCategory,
                "Resolution",
                new[] { StandardSettingIds.Width, StandardSettingIds.Height },
                text.Resolution,
                controls,
                values,
                labels);
            AddChoice(
                displayCategory,
                "Refresh Rate",
                new[] { StandardSettingIds.RefreshRate },
                text.RefreshRate,
                controls,
                values,
                labels);
            AddToggle(
                displayCategory,
                "VSync",
                StandardSettingIds.VSyncCount,
                text.VerticalSync,
                controls,
                labels);
            AddSlider(
                displayCategory,
                "Frame Rate",
                StandardSettingIds.FrameRateLimit,
                text.FrameRateLimit,
                _profile.FrameRateRange,
                controls,
                values,
                labels,
                true);
            AddChoice(
                displayCategory,
                "Quality",
                new[] { StandardSettingIds.Quality },
                text.Quality,
                controls,
                values,
                labels);

            SettingsUiCategoryView audioCategory =
                categories[StandardSettingsUiCategory.Audio];
            AddSlider(
                audioCategory,
                "Master Volume",
                StandardSettingIds.MasterVolume,
                text.MasterVolume,
                _profile.VolumeRange,
                controls,
                values,
                labels);
            AddSlider(
                audioCategory,
                "Music Volume",
                StandardSettingIds.MusicVolume,
                text.MusicVolume,
                _profile.VolumeRange,
                controls,
                values,
                labels);
            AddSlider(
                audioCategory,
                "SFX Volume",
                StandardSettingIds.SfxVolume,
                text.SfxVolume,
                _profile.VolumeRange,
                controls,
                values,
                labels);

            SettingsUiCategoryView accessibilityCategory =
                categories[StandardSettingsUiCategory.Accessibility];
            AddSlider(
                accessibilityCategory,
                "UI Scale",
                StandardSettingIds.UiScale,
                text.UiScale,
                _profile.UiScaleRange,
                controls,
                values,
                labels);
            AddToggle(
                accessibilityCategory,
                "High Contrast UI",
                StandardSettingIds.HighContrast,
                text.HighContrast,
                controls,
                labels);
            AddToggle(
                accessibilityCategory,
                "Reduce UI Motion",
                StandardSettingIds.ReduceMotion,
                text.ReduceMotion,
                controls,
                labels);
            AddChoice(
                accessibilityCategory,
                "Language",
                new[] { StandardSettingIds.Locale },
                text.Language,
                controls,
                values,
                labels);

            Button resetButton = _layout.CreateFooterButton(
                shell,
                "Restore Category Defaults",
                text.RestoreDefaults,
                false);
            Button saveButton = _layout.CreateFooterButton(
                shell,
                "Save and Back",
                text.SaveAndBack,
                true);
            var view = new StandardSettingsUiView(
                _layout,
                shell,
                tabs,
                categories,
                controls,
                values,
                labels,
                FindText(shell.Root, "Header/Title"),
                FindText(shell.Root, "Header/Subtitle"),
                resetButton,
                saveButton);
            foreach (KeyValuePair<StandardSettingsUiCategory, Button> pair in tabs)
            {
                StandardSettingsUiCategory category = pair.Key;
                pair.Value.onClick.AddListener(() => view.ShowCategory(category, true));
            }

            view.ApplyText(text);
            view.ShowCategory(StandardSettingsUiCategory.Controls);
            view.Rebuild();
            return view;
        }

        private void AddSlider(
            SettingsUiCategoryView category,
            string name,
            SettingId id,
            string label,
            Vector2 range,
            Dictionary<SettingId, Selectable> controls,
            Dictionary<SettingId, Text> values,
            Dictionary<SettingId, Text> labels,
            bool wholeNumbers = false)
        {
            Slider slider = _layout.CreateSliderRow(category, name, label, out Text value);
            slider.minValue = range.x;
            slider.maxValue = range.y;
            slider.wholeNumbers = wholeNumbers;
            Register(
                new[] { id },
                slider,
                value,
                FindLabel(category, name),
                controls,
                values,
                labels);
        }

        private void AddSlider(
            SettingsUiCategoryView category,
            string name,
            SettingId id,
            string label,
            Vector2Int range,
            Dictionary<SettingId, Selectable> controls,
            Dictionary<SettingId, Text> values,
            Dictionary<SettingId, Text> labels,
            bool wholeNumbers)
        {
            AddSlider(
                category,
                name,
                id,
                label,
                new Vector2(range.x, range.y),
                controls,
                values,
                labels,
                wholeNumbers);
        }

        private void AddToggle(
            SettingsUiCategoryView category,
            string name,
            SettingId id,
            string label,
            Dictionary<SettingId, Selectable> controls,
            Dictionary<SettingId, Text> labels)
        {
            Toggle toggle = _layout.CreateToggleRow(category, name, label);
            Register(
                new[] { id },
                toggle,
                null,
                FindLabel(category, name),
                controls,
                null,
                labels);
        }

        private void AddChoice(
            SettingsUiCategoryView category,
            string name,
            IReadOnlyList<SettingId> ids,
            string label,
            Dictionary<SettingId, Selectable> controls,
            Dictionary<SettingId, Text> values,
            Dictionary<SettingId, Text> labels)
        {
            Button button = _layout.CreateChoiceRow(category, name, label, out Text value);
            Register(
                ids,
                button,
                value,
                FindLabel(category, name),
                controls,
                values,
                labels);
        }

        private static void Register(
            IReadOnlyList<SettingId> ids,
            Selectable control,
            Text value,
            Text label,
            Dictionary<SettingId, Selectable> controls,
            Dictionary<SettingId, Text> values,
            Dictionary<SettingId, Text> labels)
        {
            foreach (SettingId id in ids)
            {
                controls.Add(id, control);
                labels.Add(id, label);
                if (value != null && values != null)
                {
                    values.Add(id, value);
                }
            }
        }

        private static Text FindLabel(SettingsUiCategoryView category, string name) =>
            FindText(category.Content, $"{name} Row/{name} Label");

        private static Text FindText(Transform root, string path)
        {
            Transform child = root.Find(path);
            if (child == null || !child.TryGetComponent(out Text text))
            {
                throw new InvalidOperationException($"Missing settings text '{path}'.");
            }

            return text;
        }
    }
}
