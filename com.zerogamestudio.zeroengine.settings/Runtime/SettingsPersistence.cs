using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Save;

namespace ZeroEngine.PlayerSettings
{
    [Serializable]
    public sealed class SettingsEntry
    {
        public string id;
        public string kind;
        public string value;

        public SettingsEntry Clone() => new() { id = id, kind = kind, value = value };
    }

    [Serializable]
    public sealed class SettingsDocument
    {
        public int formatVersion = 1;
        public List<SettingsEntry> entries = new();

        public SettingsDocument Clone()
        {
            var clone = new SettingsDocument { formatVersion = formatVersion };
            foreach (var entry in entries)
            {
                clone.entries.Add(entry.Clone());
            }

            return clone;
        }
    }

    public enum SettingsStoreSource
    {
        None,
        Primary,
        Backup
    }

    public readonly struct SettingsStoreLoadResult
    {
        private SettingsStoreLoadResult(bool success, SettingsDocument document, SettingsStoreSource source, string error)
        {
            Success = success;
            Document = document;
            Source = source;
            Error = error;
        }

        public bool Success { get; }
        public SettingsDocument Document { get; }
        public SettingsStoreSource Source { get; }
        public string Error { get; }
        public static SettingsStoreLoadResult Loaded(SettingsDocument document, SettingsStoreSource source) =>
            new(true, document, source, null);
        public static SettingsStoreLoadResult Missing() => new(true, null, SettingsStoreSource.None, null);
        public static SettingsStoreLoadResult Failed(string error) => new(false, null, SettingsStoreSource.None, error);
    }

    public readonly struct SettingsStoreSaveResult
    {
        private SettingsStoreSaveResult(bool success, string error)
        {
            Success = success;
            Error = error;
        }

        public bool Success { get; }
        public string Error { get; }
        public static SettingsStoreSaveResult Saved() => new(true, null);
        public static SettingsStoreSaveResult Failed(string error) => new(false, error);
    }

    public interface ISettingsStore
    {
        SettingsStoreLoadResult Load();
        SettingsStoreSaveResult Save(SettingsDocument document);
    }

    public static class SettingsDocumentSerializer
    {
        public static string Serialize(SettingsDocument document)
        {
            if (!TryValidate(document, out var error))
            {
                throw new ArgumentException(error, nameof(document));
            }

            return JsonUtility.ToJson(document);
        }

        public static bool TryDeserialize(string json, out SettingsDocument document, out string error)
        {
            document = null;
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "empty";
                return false;
            }

            try
            {
                document = JsonUtility.FromJson<SettingsDocument>(json);
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name;
                return false;
            }

            if (!TryValidate(document, out error))
            {
                document = null;
                return false;
            }

            return true;
        }

        public static bool TryValidate(SettingsDocument document, out string error)
        {
            if (document == null || document.formatVersion < 1 || document.entries == null)
            {
                error = "invalid-document";
                return false;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in document.entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.id) || !ids.Add(entry.id) ||
                    !Enum.TryParse(entry.kind, false, out SettingValueKind kind) ||
                    !SettingValue.TryParse(kind, entry.value, out _))
                {
                    error = "invalid-entry";
                    return false;
                }
            }

            error = null;
            return true;
        }
    }

    public sealed class PlayerPrefsSettingsStore : ISettingsStore
    {
        private readonly string _primaryKey;
        private readonly string _backupKey;

        public PlayerPrefsSettingsStore(string keyPrefix = "ZeroEngine.Settings")
        {
            if (string.IsNullOrWhiteSpace(keyPrefix))
            {
                throw new ArgumentException("Key prefix cannot be empty.", nameof(keyPrefix));
            }

            _primaryKey = keyPrefix + ".Primary";
            _backupKey = keyPrefix + ".Backup";
        }

        public SettingsStoreLoadResult Load()
        {
            if (TryLoadKey(_primaryKey, out var primary))
            {
                return SettingsStoreLoadResult.Loaded(primary, SettingsStoreSource.Primary);
            }

            if (TryLoadKey(_backupKey, out var backup))
            {
                return SettingsStoreLoadResult.Loaded(backup, SettingsStoreSource.Backup);
            }

            return PlayerPrefs.HasKey(_primaryKey) || PlayerPrefs.HasKey(_backupKey)
                ? SettingsStoreLoadResult.Failed("primary-and-backup-invalid")
                : SettingsStoreLoadResult.Missing();
        }

        public SettingsStoreSaveResult Save(SettingsDocument document)
        {
            string json;
            try
            {
                json = SettingsDocumentSerializer.Serialize(document);
            }
            catch (Exception exception)
            {
                return SettingsStoreSaveResult.Failed(exception.GetType().Name);
            }

            try
            {
                if (PlayerPrefs.HasKey(_primaryKey))
                {
                    var oldPrimary = PlayerPrefs.GetString(_primaryKey);
                    if (SettingsDocumentSerializer.TryDeserialize(oldPrimary, out _, out _))
                    {
                        PlayerPrefs.SetString(_backupKey, oldPrimary);
                    }
                }

                PlayerPrefs.SetString(_primaryKey, json);
                PlayerPrefs.Save();
                return SettingsStoreSaveResult.Saved();
            }
            catch (Exception exception)
            {
                return SettingsStoreSaveResult.Failed(exception.GetType().Name);
            }
        }

        private static bool TryLoadKey(string key, out SettingsDocument document)
        {
            document = null;
            return PlayerPrefs.HasKey(key) &&
                   SettingsDocumentSerializer.TryDeserialize(PlayerPrefs.GetString(key), out document, out _);
        }
    }

    public sealed class SaveManagerSettingsStore : ISettingsStore
    {
        private readonly SaveManager _saveManager;
        private readonly string _primaryKey;
        private readonly string _backupKey;

        public SaveManagerSettingsStore(
            SaveManager saveManager,
            string keyPrefix = "ZeroEngine.Settings")
        {
            _saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
            _primaryKey = keyPrefix + ".Primary";
            _backupKey = keyPrefix + ".Backup";
        }

        public SettingsStoreLoadResult Load()
        {
            if (TryLoad(_primaryKey, out var primary))
            {
                return SettingsStoreLoadResult.Loaded(primary, SettingsStoreSource.Primary);
            }

            if (TryLoad(_backupKey, out var backup))
            {
                return SettingsStoreLoadResult.Loaded(backup, SettingsStoreSource.Backup);
            }

            return _saveManager.Exists(_primaryKey, SaveManager.SettingsFile) ||
                   _saveManager.Exists(_backupKey, SaveManager.SettingsFile)
                ? SettingsStoreLoadResult.Failed("primary-and-backup-invalid")
                : SettingsStoreLoadResult.Missing();
        }

        public SettingsStoreSaveResult Save(SettingsDocument document)
        {
            try
            {
                var json = SettingsDocumentSerializer.Serialize(document);
                if (_saveManager.Exists(_primaryKey, SaveManager.SettingsFile))
                {
                    var oldPrimary = _saveManager.Load(_primaryKey, string.Empty, SaveManager.SettingsFile);
                    if (SettingsDocumentSerializer.TryDeserialize(oldPrimary, out _, out _))
                    {
                        _saveManager.Save(_backupKey, oldPrimary, SaveManager.SettingsFile);
                    }
                }

                _saveManager.Save(_primaryKey, json, SaveManager.SettingsFile);
                return SettingsStoreSaveResult.Saved();
            }
            catch (Exception exception)
            {
                return SettingsStoreSaveResult.Failed(exception.GetType().Name);
            }
        }

        private bool TryLoad(string key, out SettingsDocument document)
        {
            document = null;
            return _saveManager.Exists(key, SaveManager.SettingsFile) &&
                   SettingsDocumentSerializer.TryDeserialize(
                       _saveManager.Load(key, string.Empty, SaveManager.SettingsFile),
                       out document,
                       out _);
        }
    }

    public interface ISettingsMigration
    {
        int FromVersion { get; }
        int ToVersion { get; }
        bool TryMigrate(SettingsDocument document, out SettingsDocument migrated, out string error);
    }
}
