using UnityEngine;
using ZeroEngine.Core;

#if UNITY_LOCALIZATION
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using System.Collections.Generic;
#endif

namespace ZeroEngine.Localization
{
    /// <summary>
    /// Simplified wrapper for Unity Localization.
    /// Requires com.unity.localization package.
    /// </summary>
    public class LocalizationManager : Singleton<LocalizationManager>
    {
#if UNITY_LOCALIZATION
        /// <summary>
        /// Current selected locale.
        /// </summary>
        public Locale CurrentLocale => LocalizationSettings.SelectedLocale;

        /// <summary>
        /// All available locales.
        /// </summary>
        public IList<Locale> AvailableLocales => LocalizationSettings.AvailableLocales.Locales;

        /// <summary>
        /// Event triggered when locale changes.
        /// </summary>
        public event System.Action<Locale> OnLocaleChanged;

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;

            LocalizationSettings.SelectedLocaleChanged += HandleLocaleChanged;
        }

        protected override void OnDestroy()
        {
            LocalizationSettings.SelectedLocaleChanged -= HandleLocaleChanged;
            base.OnDestroy();
        }

        private void HandleLocaleChanged(Locale locale)
        {
            OnLocaleChanged?.Invoke(locale);
            EventManager.Trigger("Localization.Changed", locale);
        }

        /// <summary>
        /// Set locale by language code (e.g., "en", "zh-Hans", "ja").
        /// </summary>
        public void SetLocale(string languageCode)
        {
            foreach (var locale in AvailableLocales)
            {
                if (locale.Identifier.Code == languageCode)
                {
                    LocalizationSettings.SelectedLocale = locale;
                    return;
                }
            }
            Debug.LogWarning($"[LocalizationManager] Locale not found: {languageCode}");
        }

        /// <summary>
        /// Set locale by Locale object.
        /// </summary>
        public void SetLocale(Locale locale)
        {
            LocalizationSettings.SelectedLocale = locale;
        }

        /// <summary>
        /// Get localized string from default String Table.
        /// </summary>
        public string GetString(string tableName, string entryName)
        {
            var op = LocalizationSettings.StringDatabase.GetLocalizedStringAsync(tableName, entryName);
            if (op.IsDone)
                return string.IsNullOrEmpty(op.Result) ? MissingKeyFormatter.Format(entryName) : op.Result;

            // The legacy synchronous API remains non-blocking for frame safety.
            return MissingKeyFormatter.Format(entryName);
        }

        /// <summary>
        /// Get localized string with arguments.
        /// </summary>
        public string GetString(string tableName, string entryName, params object[] args)
        {
            var op = LocalizationSettings.StringDatabase.GetLocalizedStringAsync(tableName, entryName, args);
            if (op.IsDone)
                return string.IsNullOrEmpty(op.Result) ? MissingKeyFormatter.Format(entryName) : op.Result;

            return MissingKeyFormatter.Format(entryName);
        }

        /// <summary>
        /// Get localized string using TableEntryReference (for LocalizedString fields).
        /// </summary>
        public string GetString(LocalizedString localizedString)
        {
            if (localizedString.IsEmpty)
                return string.Empty;
            
            var op = localizedString.GetLocalizedStringAsync();
            if (op.IsDone)
                return string.IsNullOrEmpty(op.Result)
                    ? MissingKeyFormatter.Format(localizedString.TableEntryReference.Key)
                    : op.Result;

            return MissingKeyFormatter.Format(localizedString.TableEntryReference.Key);
        }

#else
        // Stub implementation when Unity Localization is not installed
        
        public object CurrentLocale => null;
        
        public event System.Action<object> OnLocaleChanged;

        public void SetLocale(string languageCode)
        {
            Debug.LogWarning("[LocalizationManager] Unity Localization package is not installed.");
        }

        public string GetString(string tableName, string entryName)
        {
            Debug.LogWarning("[LocalizationManager] Unity Localization package is not installed.");
            return $"[{tableName}/{entryName}]";
        }

        public string GetString(string tableName, string entryName, params object[] args)
        {
            Debug.LogWarning("[LocalizationManager] Unity Localization package is not installed.");
            return $"[{tableName}/{entryName}]";
        }
#endif
    }
}
