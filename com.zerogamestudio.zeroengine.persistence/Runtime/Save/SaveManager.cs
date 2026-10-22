using UnityEngine;
using ZeroEngine.Core;
using ZeroEngine.Utils;
using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;

#if ES3
using ES3Internal;
#endif

namespace ZeroEngine.Save
{
    /// <summary>
    /// Save provider interface for implementing different backends.
    /// </summary>
    public interface ISaveProvider
    {
        void Save<T>(string key, T data, string fileName);
        T Load<T>(string key, T defaultValue, string fileName);
        bool Exists(string key, string fileName);
        void DeleteKey(string key, string fileName);
        void DeleteFile(string fileName);
        byte[] LoadBytes(string key, string fileName);
        void SaveBytes(string key, byte[] bytes, string fileName);
    }

    /// <summary>
    /// Pure JSON save provider - no external dependencies.
    /// Uses Unity's JsonUtility with wrapper for complex types.
    /// </summary>
    public class JsonSaveProvider : ISaveProvider
    {
        private readonly Dictionary<string, Dictionary<string, string>> _cache = new();
        private readonly string _basePath;

        public JsonSaveProvider()
        {
            _basePath = Application.persistentDataPath;
        }

        private string GetFilePath(string fileName)
        {
            // Change .es3 extension to .json for clarity
            if (fileName.EndsWith(".es3"))
                fileName = fileName.Replace(".es3", ".json");
            return Path.Combine(_basePath, fileName);
        }

        private Dictionary<string, string> LoadFileCache(string fileName)
        {
            if (_cache.TryGetValue(fileName, out var cached))
                return cached;

            var filePath = GetFilePath(fileName);
            if (File.Exists(filePath))
            {
                try
                {
                    var json = File.ReadAllText(filePath);
                    var wrapper = JsonUtility.FromJson<SaveFileWrapper>(json);
                    var dict = new Dictionary<string, string>();
                    if (wrapper?.entries != null)
                    {
                        foreach (var entry in wrapper.entries)
                        {
                            dict[entry.key] = entry.value;
                        }
                    }
                    _cache[fileName] = dict;
                    return dict;
                }
                catch (Exception e)
                {
                    ZeroLog.Error(ZeroLog.Modules.Save, $"Failed to load {filePath}: {e.Message}");
                }
            }

            var newDict = new Dictionary<string, string>();
            _cache[fileName] = newDict;
            return newDict;
        }

        private void SaveFileCache(string fileName)
        {
            if (!_cache.TryGetValue(fileName, out var dict))
                return;

            var wrapper = new SaveFileWrapper();
            wrapper.entries = new List<SaveEntry>();
            foreach (var kvp in dict)
            {
                wrapper.entries.Add(new SaveEntry { key = kvp.Key, value = kvp.Value });
            }

            var json = JsonUtility.ToJson(wrapper, true);
            var filePath = GetFilePath(fileName);

            try
            {
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(filePath, json);
            }
            catch (Exception e)
            {
                ZeroLog.Error(ZeroLog.Modules.Save, $"Failed to save {filePath}: {e.Message}");
            }
        }

        public void Save<T>(string key, T data, string fileName)
        {
            var dict = LoadFileCache(fileName);
            var json = SerializeValue(data);
            dict[key] = json;
            SaveFileCache(fileName);
        }

        public T Load<T>(string key, T defaultValue, string fileName)
        {
            var dict = LoadFileCache(fileName);
            if (dict.TryGetValue(key, out var json))
            {
                try
                {
                    return DeserializeValue<T>(json);
                }
                catch (Exception e)
                {
                    ZeroLog.Warning(ZeroLog.Modules.Save, $"Failed to deserialize key '{key}': {e.Message}");
                }
            }
            return defaultValue;
        }

        public bool Exists(string key, string fileName)
        {
            var dict = LoadFileCache(fileName);
            return dict.ContainsKey(key);
        }

        public void DeleteKey(string key, string fileName)
        {
            var dict = LoadFileCache(fileName);
            if (dict.Remove(key))
            {
                SaveFileCache(fileName);
            }
        }

        public void DeleteFile(string fileName)
        {
            _cache.Remove(fileName);
            var filePath = GetFilePath(fileName);
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch (Exception e)
                {
                    ZeroLog.Error(ZeroLog.Modules.Save, $"Failed to delete {filePath}: {e.Message}");
                }
            }
        }

        public byte[] LoadBytes(string key, string fileName)
        {
            var dict = LoadFileCache(fileName);
            if (dict.TryGetValue(key, out var base64))
            {
                try
                {
                    return Convert.FromBase64String(base64);
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        public void SaveBytes(string key, byte[] bytes, string fileName)
        {
            var dict = LoadFileCache(fileName);
            dict[key] = Convert.ToBase64String(bytes);
            SaveFileCache(fileName);
        }

        // Serialization helpers
        private string SerializeValue<T>(T value)
        {
            if (value == null) return "null";

            if (value is Dictionary<string, object> objectDictionary)
                return SerializeObjectDictionary(objectDictionary);

            var type = typeof(T);

            // Handle primitives and strings directly
            if (type == typeof(string)) return value.ToString();
            if (type == typeof(int) || type == typeof(float) || type == typeof(double) ||
                type == typeof(bool) || type == typeof(long))
            {
                return value.ToString();
            }

            // Use JsonUtility for complex types
            // Wrap in container for non-serializable root types
            var wrapper = new ValueWrapper<T> { value = value };
            return JsonUtility.ToJson(wrapper);
        }

        private T DeserializeValue<T>(string json)
        {
            if (string.IsNullOrEmpty(json) || json == "null")
                return default;

            var type = typeof(T);

            if (type == typeof(Dictionary<string, object>))
                return (T)(object)DeserializeObjectDictionary(json);

            // Handle primitives
            if (type == typeof(string)) return (T)(object)json;
            if (type == typeof(int)) return (T)(object)int.Parse(json);
            if (type == typeof(float)) return (T)(object)float.Parse(json);
            if (type == typeof(double)) return (T)(object)double.Parse(json);
            if (type == typeof(bool)) return (T)(object)bool.Parse(json);
            if (type == typeof(long)) return (T)(object)long.Parse(json);

            // Complex types
            var wrapper = JsonUtility.FromJson<ValueWrapper<T>>(json);
            return wrapper.value;
        }

        private string SerializeObjectDictionary(Dictionary<string, object> values)
        {
            var wrapper = new ObjectDictionaryWrapper
            {
                entries = new List<ObjectDictionaryEntry>()
            };

            foreach (var kvp in values)
            {
                var entry = new ObjectDictionaryEntry { key = kvp.Key };
                if (kvp.Value != null)
                {
                    var valueType = kvp.Value.GetType();
                    entry.typeName = valueType.AssemblyQualifiedName;
                    entry.value = SerializeBoxedValue(kvp.Value, valueType);
                }

                wrapper.entries.Add(entry);
            }

            return JsonUtility.ToJson(wrapper);
        }

        private Dictionary<string, object> DeserializeObjectDictionary(string json)
        {
            var result = new Dictionary<string, object>();
            var wrapper = JsonUtility.FromJson<ObjectDictionaryWrapper>(json);
            if (wrapper?.entries == null)
                return result;

            foreach (var entry in wrapper.entries)
            {
                if (string.IsNullOrEmpty(entry.key))
                    continue;

                if (string.IsNullOrEmpty(entry.typeName))
                {
                    result[entry.key] = null;
                    continue;
                }

                var valueType = Type.GetType(entry.typeName);
                if (valueType == null)
                {
                    ZeroLog.Warning(ZeroLog.Modules.Save, $"Failed to resolve save data type '{entry.typeName}' for key '{entry.key}'.");
                    continue;
                }

                result[entry.key] = DeserializeBoxedValue(entry.value, valueType);
            }

            return result;
        }

        private string SerializeBoxedValue(object value, Type type)
        {
            if (type == typeof(string)) return value.ToString();
            if (type == typeof(int) || type == typeof(float) || type == typeof(double) ||
                type == typeof(bool) || type == typeof(long))
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            return JsonUtility.ToJson(value);
        }

        private object DeserializeBoxedValue(string value, Type type)
        {
            if (type == typeof(string)) return value;
            if (type == typeof(int)) return int.Parse(value, CultureInfo.InvariantCulture);
            if (type == typeof(float)) return float.Parse(value, CultureInfo.InvariantCulture);
            if (type == typeof(double)) return double.Parse(value, CultureInfo.InvariantCulture);
            if (type == typeof(bool)) return bool.Parse(value);
            if (type == typeof(long)) return long.Parse(value, CultureInfo.InvariantCulture);

            return JsonUtility.FromJson(value, type);
        }

        [Serializable]
        private class SaveFileWrapper
        {
            public List<SaveEntry> entries;
        }

        [Serializable]
        private class SaveEntry
        {
            public string key;
            public string value;
        }

        [Serializable]
        private class ValueWrapper<T>
        {
            public T value;
        }

        [Serializable]
        private class ObjectDictionaryWrapper
        {
            public List<ObjectDictionaryEntry> entries;
        }

        [Serializable]
        private class ObjectDictionaryEntry
        {
            public string key;
            public string typeName;
            public string value;
        }
    }

#if ES3
    /// <summary>
    /// ES3 save provider wrapper.
    /// </summary>
    public class ES3SaveProvider : ISaveProvider
    {
        public void Save<T>(string key, T data, string fileName) => ES3.Save(key, data, fileName);
        public T Load<T>(string key, T defaultValue, string fileName) => ES3.Load(key, fileName, defaultValue);
        public bool Exists(string key, string fileName) => ES3.KeyExists(key, fileName);
        public void DeleteKey(string key, string fileName) => ES3.DeleteKey(key, fileName);
        public void DeleteFile(string fileName) => ES3.DeleteFile(fileName);
        public byte[] LoadBytes(string key, string fileName) => ES3.KeyExists(key, fileName) ? ES3.Load<byte[]>(key, fileName) : null;
        public void SaveBytes(string key, byte[] bytes, string fileName) => ES3.Save(key, bytes, fileName);
    }
#endif

    public class SaveManager : Singleton<SaveManager>
    {
        public const string DefaultSaveFile = "SaveData.es3";
        public const string SettingsFile = "Settings.es3";
        public const string GlobalDataFile = "GlobalData.es3";

        private ISaveProvider _provider;

        /// <summary>
        /// The active save provider. Auto-selects ES3 if available, otherwise JSON.
        /// </summary>
        public ISaveProvider Provider
        {
            get
            {
                if (_provider == null)
                {
#if ES3
                    _provider = new ES3SaveProvider();
                    ZeroLog.Info(ZeroLog.Modules.Save, "Using ES3 provider.");
#else
                    _provider = new JsonSaveProvider();
                    ZeroLog.Info(ZeroLog.Modules.Save, "Using JSON provider (ES3 not available).");
#endif
                }
                return _provider;
            }
            set => _provider = value;
        }

        /// <summary>
        /// Force use JSON provider even if ES3 is available.
        /// </summary>
        public void UseJsonProvider()
        {
            _provider = new JsonSaveProvider();
        }

        /// <summary>
        /// Save data to key.
        /// </summary>
        public void Save<T>(string key, T data, string fileName = DefaultSaveFile)
        {
            Provider.Save(key, data, fileName);
        }

        /// <summary>
        /// Load data from key.
        /// </summary>
        public T Load<T>(string key, T defaultValue = default, string fileName = DefaultSaveFile)
        {
            return Provider.Load(key, defaultValue, fileName);
        }

        /// <summary>
        /// Check if key exists.
        /// </summary>
        public bool Exists(string key, string fileName = DefaultSaveFile)
        {
            return Provider.Exists(key, fileName);
        }

        /// <summary>
        /// Delete specific key.
        /// </summary>
        public void DeleteKey(string key, string fileName = DefaultSaveFile)
        {
            Provider.DeleteKey(key, fileName);
        }

        /// <summary>
        /// Delete entire file.
        /// </summary>
        public void DeleteFile(string fileName = DefaultSaveFile)
        {
            Provider.DeleteFile(fileName);
        }

        public byte[] LoadImage(string key, string fileName = DefaultSaveFile)
        {
            return Provider.LoadBytes(key, fileName);
        }

        public void SaveImage(string key, byte[] bytes, string fileName = DefaultSaveFile)
        {
            Provider.SaveBytes(key, bytes, fileName);
        }
    }
}
