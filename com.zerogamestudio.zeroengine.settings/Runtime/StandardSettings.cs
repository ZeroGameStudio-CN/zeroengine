using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Localization.Settings;
using ZeroEngine.Audio;
using ZeroEngine.InputSystem;

namespace ZeroEngine.PlayerSettings
{
    public static class StandardSettingIds
    {
        public static readonly SettingId Locale = new("localization.locale");
        public static readonly SettingId WindowMode = new("display.windowMode");
        public static readonly SettingId Width = new("display.width");
        public static readonly SettingId Height = new("display.height");
        public static readonly SettingId RefreshRate = new("display.refreshRate");
        public static readonly SettingId VSyncCount = new("display.vSyncCount");
        public static readonly SettingId FrameRateLimit = new("display.frameRateLimit");
        public static readonly SettingId Quality = new("display.quality");
        public static readonly SettingId MasterVolume = new("audio.master");
        public static readonly SettingId MusicVolume = new("audio.music");
        public static readonly SettingId SfxVolume = new("audio.sfx");
        public static readonly SettingId PointerSensitivity = new("input.pointerSensitivity");
        public static readonly SettingId GamepadSensitivity = new("input.gamepadSensitivity");
        public static readonly SettingId GamepadDeadzone = new("input.gamepadDeadzone");
        public static readonly SettingId InvertY = new("input.invertY");
        public static readonly SettingId Vibration = new("input.vibration");
        public static readonly SettingId GlyphStyle = new("input.glyphStyle");
        public static readonly SettingId BindingOverrides = new("input.bindingOverrides");
        public static readonly SettingId UiScale = new("accessibility.uiScale");
        public static readonly SettingId HighContrast = new("accessibility.highContrast");
        public static readonly SettingId ReduceMotion = new("accessibility.reduceMotion");
    }

    public sealed class StandardSettingsDefaults
    {
        public string LocaleCode = "en";
        public FullScreenMode WindowMode = FullScreenMode.FullScreenWindow;
        public int Width = 1920;
        public int Height = 1080;
        public int RefreshRate;
        public int VSyncCount = 1;
        public int FrameRateLimit = -1;
        public string QualityName = "High";
        public float MasterVolume = 1f;
        public float MusicVolume = 1f;
        public float SfxVolume = 1f;
        public float PointerSensitivity = 1f;
        public float GamepadSensitivity = 1f;
        public float GamepadDeadzone = 0.125f;
        public bool InvertY;
        public float Vibration = 1f;
        public string GlyphStyle = "Auto";
        public string BindingOverrides = string.Empty;
        public float UiScale = 1f;
        public bool HighContrast;
        public bool ReduceMotion;
    }

    public static class StandardSettingsCatalog
    {
        public static IReadOnlyList<SettingDefinition> Create(
            StandardSettingsDefaults defaults,
            Func<IReadOnlyList<string>> localeOptions,
            Func<IReadOnlyList<string>> qualityOptions)
        {
            defaults ??= new StandardSettingsDefaults();
            localeOptions ??= () => new[] { defaults.LocaleCode };
            qualityOptions ??= () => QualitySettings.names;
            return new[]
            {
                Text(StandardSettingIds.Locale, "language", defaults.LocaleCode, SettingApplyPolicy.Preview, localeOptions),
                Text(StandardSettingIds.WindowMode, "display", defaults.WindowMode.ToString(), SettingApplyPolicy.Preview,
                    () => Enum.GetNames(typeof(FullScreenMode))),
                Int(StandardSettingIds.Width, "display", defaults.Width, 320, 16384, SettingApplyPolicy.Preview),
                Int(StandardSettingIds.Height, "display", defaults.Height, 200, 16384, SettingApplyPolicy.Preview),
                Int(StandardSettingIds.RefreshRate, "display", defaults.RefreshRate, 0, 1000, SettingApplyPolicy.Preview),
                Int(StandardSettingIds.VSyncCount, "display", defaults.VSyncCount, 0, 4, SettingApplyPolicy.Preview),
                Int(StandardSettingIds.FrameRateLimit, "display", defaults.FrameRateLimit, -1, 1000, SettingApplyPolicy.Preview),
                Text(StandardSettingIds.Quality, "display", defaults.QualityName, SettingApplyPolicy.Preview, qualityOptions),
                Float(StandardSettingIds.MasterVolume, "audio", defaults.MasterVolume, 0f, 1f),
                Float(StandardSettingIds.MusicVolume, "audio", defaults.MusicVolume, 0f, 1f),
                Float(StandardSettingIds.SfxVolume, "audio", defaults.SfxVolume, 0f, 1f),
                Float(StandardSettingIds.PointerSensitivity, "controls", defaults.PointerSensitivity, 0.05f, 10f),
                Float(StandardSettingIds.GamepadSensitivity, "controls", defaults.GamepadSensitivity, 0.05f, 10f),
                Float(StandardSettingIds.GamepadDeadzone, "controls", defaults.GamepadDeadzone, 0f, 0.95f),
                Bool(StandardSettingIds.InvertY, "controls", defaults.InvertY),
                Float(StandardSettingIds.Vibration, "controls", defaults.Vibration, 0f, 1f),
                Text(StandardSettingIds.GlyphStyle, "controls", defaults.GlyphStyle, SettingApplyPolicy.Preview,
                    () => Enum.GetNames(typeof(GamepadGlyphStyle))),
                new SettingDefinition(StandardSettingIds.BindingOverrides, "controls", SettingValue.String(defaults.BindingOverrides),
                    SettingApplyPolicy.Preview, "settings.input.bindingOverrides", visible: false),
                Float(StandardSettingIds.UiScale, "accessibility", defaults.UiScale, 0.5f, 2f),
                Bool(StandardSettingIds.HighContrast, "accessibility", defaults.HighContrast),
                Bool(StandardSettingIds.ReduceMotion, "accessibility", defaults.ReduceMotion)
            };
        }

        private static SettingDefinition Bool(SettingId id, string category, bool value) =>
            new(id, category, SettingValue.Bool(value), SettingApplyPolicy.Preview, "settings." + id.Value);

        private static SettingDefinition Int(
            SettingId id, string category, int value, int min, int max, SettingApplyPolicy policy) =>
            new(id, category, SettingValue.Int(value), policy, "settings." + id.Value,
                validator: candidate => candidate.AsInt() >= min && candidate.AsInt() <= max);

        private static SettingDefinition Float(
            SettingId id, string category, float value, float min, float max) =>
            new(id, category, SettingValue.Float(value), SettingApplyPolicy.Preview, "settings." + id.Value,
                validator: candidate => candidate.AsFloat() >= min && candidate.AsFloat() <= max);

        private static SettingDefinition Text(
            SettingId id,
            string category,
            string value,
            SettingApplyPolicy policy,
            Func<IReadOnlyList<string>> options) =>
            new(id, category, SettingValue.String(value), policy, "settings." + id.Value, optionProvider: options);
    }

    public sealed class AudioSettingsApplier : ISettingApplier
    {
        private static readonly SettingId[] Ids =
            { StandardSettingIds.MasterVolume, StandardSettingIds.MusicVolume, StandardSettingIds.SfxVolume };
        private readonly AudioManager _audioManager;

        public AudioSettingsApplier(AudioManager audioManager)
        {
            _audioManager = audioManager ?? throw new ArgumentNullException(nameof(audioManager));
            _audioManager.SetVolumePersistenceEnabled(false);
        }

        public IReadOnlyCollection<SettingId> SettingIds => Ids;

        public Task<SettingApplyResult> ApplyAsync(SettingsSnapshot snapshot, CancellationToken cancellationToken)
        {
            _audioManager.SetMasterVolume(snapshot[StandardSettingIds.MasterVolume].AsFloat());
            _audioManager.SetBGMVolume(snapshot[StandardSettingIds.MusicVolume].AsFloat());
            _audioManager.SetSFXVolume(snapshot[StandardSettingIds.SfxVolume].AsFloat());
            return Task.FromResult(SettingApplyResult.Applied());
        }
    }

    public sealed class LocalizationSettingsApplier : ISettingApplier
    {
        private static readonly SettingId[] Ids = { StandardSettingIds.Locale };
        public IReadOnlyCollection<SettingId> SettingIds => Ids;

        public async Task<SettingApplyResult> ApplyAsync(SettingsSnapshot snapshot, CancellationToken cancellationToken)
        {
            try
            {
                await LocalizationSettings.InitializationOperation.Task;
                cancellationToken.ThrowIfCancellationRequested();
                var code = snapshot[StandardSettingIds.Locale].AsString();
                var locale = LocalizationSettings.AvailableLocales.Locales.FirstOrDefault(
                    x => string.Equals(x.Identifier.Code, code, StringComparison.Ordinal));
                if (locale == null)
                {
                    return SettingApplyResult.Failed("locale-unavailable");
                }

                LocalizationSettings.SelectedLocale = locale;
                return SettingApplyResult.Applied();
            }
            catch (Exception exception)
            {
                return SettingApplyResult.Failed(exception.GetType().Name);
            }
        }
    }

    public sealed class InputSettingsApplier : ISettingApplier
    {
        private static readonly SettingId[] Ids =
        {
            StandardSettingIds.PointerSensitivity, StandardSettingIds.GamepadSensitivity,
            StandardSettingIds.GamepadDeadzone, StandardSettingIds.InvertY, StandardSettingIds.Vibration,
            StandardSettingIds.GlyphStyle, StandardSettingIds.BindingOverrides
        };
        private readonly InputBindingService _bindings;
        private readonly Action<SettingsSnapshot> _applyPreferences;

        public InputSettingsApplier(InputActionAsset asset, Action<SettingsSnapshot> applyPreferences = null)
        {
            _bindings = new InputBindingService(asset);
            _applyPreferences = applyPreferences;
        }

        public IReadOnlyCollection<SettingId> SettingIds => Ids;

        public Task<SettingApplyResult> ApplyAsync(SettingsSnapshot snapshot, CancellationToken cancellationToken)
        {
            if (!_bindings.TryLoadOverrides(snapshot[StandardSettingIds.BindingOverrides].AsString()))
            {
                return Task.FromResult(SettingApplyResult.Failed("binding-overrides-invalid"));
            }

            _applyPreferences?.Invoke(snapshot);
            return Task.FromResult(SettingApplyResult.Applied());
        }
    }

    public sealed class AccessibilitySettingsApplier : ISettingApplier
    {
        private static readonly SettingId[] Ids =
            { StandardSettingIds.UiScale, StandardSettingIds.HighContrast, StandardSettingIds.ReduceMotion };
        private readonly Action<SettingsSnapshot> _apply;

        public AccessibilitySettingsApplier(Action<SettingsSnapshot> apply)
        {
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        }

        public IReadOnlyCollection<SettingId> SettingIds => Ids;

        public Task<SettingApplyResult> ApplyAsync(SettingsSnapshot snapshot, CancellationToken cancellationToken)
        {
            _apply(snapshot);
            return Task.FromResult(SettingApplyResult.Applied());
        }
    }
}
