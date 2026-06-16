using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

#if NEWTONSOFT_JSON
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#endif

namespace ZeroEngine.ModSystem
{
    /// <summary>
    /// Mod内容解析器，用于解析JSON文件并转换为Unity对象
    /// </summary>
    public class ModContentParser
    {
        private readonly ITypeRegistry _typeRegistry;
        private readonly IAssetRegistry _assetRegistry;
        
        public ModContentParser(ITypeRegistry typeRegistry, IAssetRegistry assetRegistry)
        {
            _typeRegistry = typeRegistry;
            _assetRegistry = assetRegistry;
        }
        
        /// <summary>
        /// 解析目录中的所有内容
        /// </summary>
        public void ParseDirectory(string directoryPath, LoadedMod mod)
        {
            if (!Directory.Exists(directoryPath))
            {
                Debug.LogWarning($"[ModContentParser] Directory not found: {directoryPath}");
                return;
            }
            
            // 解析所有JSON文件
            foreach (var jsonFile in Directory.GetFiles(directoryPath, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    ParseJsonFile(jsonFile, mod);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ModContentParser] Failed to parse {jsonFile}: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// 解析单个JSON文件
        /// </summary>
        public void ParseJsonFile(string filePath, LoadedMod mod)
        {
            var json = File.ReadAllText(filePath);
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var modId = mod.Manifest.Id;
            
#if NEWTONSOFT_JSON
            var jObject = JObject.Parse(json);
            
            // 检查是否有 $type 字段
            if (jObject.TryGetValue("$type", out var typeToken))
            {
                var typeName = typeToken.Value<string>();
                var instance = CreateInstanceFromJson(typeName, jObject, mod.Manifest.RootPath);
                
                if (instance != null)
                {
                    var assetKey = $"{modId}:{fileName}";
                    
                    if (instance is UnityEngine.Object unityObj)
                    {
                        _assetRegistry.RegisterAsset(assetKey, unityObj);
                        mod.LoadedAssets.Add(unityObj);
                    }
                    
                    Debug.Log($"[ModContentParser] Loaded: {assetKey} ({typeName})");
                }
            }
            else
            {
                Debug.LogWarning($"[ModContentParser] No $type field in: {filePath}");
            }
#else
            Debug.LogWarning("[ModContentParser] Newtonsoft.Json is required for advanced parsing. " +
                           "Add NEWTONSOFT_JSON to your scripting define symbols.");
#endif
        }
        
#if NEWTONSOFT_JSON
        /// <summary>
        /// 从JSON创建实例
        /// </summary>
        private object CreateInstanceFromJson(string typeName, JObject data, string modRootPath)
        {
            // 首先尝试从类型注册表创建
            var instance = _typeRegistry.CreateInstance(typeName);
            
            if (instance == null)
            {
                // 尝试作为ScriptableObject创建
                var type = _typeRegistry.GetType(typeName);
                if (type != null && typeof(ScriptableObject).IsAssignableFrom(type))
                {
                    instance = ScriptableObject.CreateInstance(type);
                }
            }
            
            if (instance == null)
            {
                Debug.LogWarning($"[ModContentParser] Unknown type: {typeName}");
                return null;
            }
            
            // 填充字段
            PopulateFields(instance, data, modRootPath);
            
            return instance;
        }
        
        /// <summary>
        /// 填充对象字段
        /// </summary>
        private void PopulateFields(object instance, JObject data, string modRootPath)
        {
            var type = instance.GetType();
            
            foreach (var property in data.Properties())
            {
                if (property.Name == "$type") continue;
                
                var field = type.GetField(property.Name, 
                    System.Reflection.BindingFlags.Public | 
                    System.Reflection.BindingFlags.Instance);
                
                if (field == null)
                {
                    var prop = type.GetProperty(property.Name,
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Instance);
                    
                    if (prop != null && prop.CanWrite)
                    {
                        var value = ConvertValue(property.Value, prop.PropertyType, modRootPath);
                        prop.SetValue(instance, value);
                    }
                }
                else
                {
                    var value = ConvertValue(property.Value, field.FieldType, modRootPath);
                    field.SetValue(instance, value);
                }
            }
        }
        
        /// <summary>
        /// 转换JSON值到目标类型
        /// </summary>
        private object ConvertValue(JToken token, Type targetType, string modRootPath)
        {
            // 处理Sprite类型 - 加载图片
            if (targetType == typeof(Sprite) && token.Type == JTokenType.String)
            {
                var path = token.Value<string>();
                return LoadSprite(Path.Combine(modRootPath, path));
            }
            
            // 处理AudioClip类型
            if (targetType == typeof(AudioClip) && token.Type == JTokenType.String)
            {
                var path = token.Value<string>();
                return LoadAudioClip(Path.Combine(modRootPath, path));
            }
            
            // 处理枚举
            if (targetType.IsEnum && token.Type == JTokenType.String)
            {
                return Enum.Parse(targetType, token.Value<string>());
            }
            
            // 处理嵌套对象
            if (token is JObject jObj)
            {
                if (jObj.TryGetValue("$type", out var typeToken))
                {
                    return CreateInstanceFromJson(typeToken.Value<string>(), jObj, modRootPath);
                }
                
                // 直接反序列化
                return token.ToObject(targetType);
            }
            
            // 处理数组/列表
            if (token is JArray jArray && targetType.IsGenericType)
            {
                var elementType = targetType.GetGenericArguments()[0];
                var list = (System.Collections.IList)Activator.CreateInstance(targetType);
                
                foreach (var item in jArray)
                {
                    list.Add(ConvertValue(item, elementType, modRootPath));
                }
                
                return list;
            }
            
            // 默认转换
            return token.ToObject(targetType);
        }
#endif
        
        /// <summary>
        /// 加载图片为Sprite
        /// </summary>
        public Sprite LoadSprite(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[ModContentParser] Image not found: {path}");
                return null;
            }
            
            var bytes = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2);
            
            if (tex.LoadImage(bytes))
            {
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f);
            }
            
            Debug.LogError($"[ModContentParser] Failed to load image: {path}");
            return null;
        }
        
        /// <summary>
        /// 加载音频文件（简化实现，完整版需要使用UnityWebRequest）
        /// </summary>
        public AudioClip LoadAudioClip(string path)
        {
            // 注意：Unity不能直接从文件加载AudioClip
            // 完整实现需要使用 UnityWebRequestMultimedia.GetAudioClip
            Debug.LogWarning($"[ModContentParser] AudioClip loading requires async implementation. Path: {path}");
            return null;
        }
    }
}
