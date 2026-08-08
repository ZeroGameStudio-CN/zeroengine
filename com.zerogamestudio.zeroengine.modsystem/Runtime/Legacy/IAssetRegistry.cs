using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.ModSystem
{
    /// <summary>
    /// 资源注册表接口。
    /// 游戏项目实现此接口，管理mod加载的资源。
    /// </summary>
    public interface IAssetRegistry
    {
        /// <summary>
        /// 注册资源
        /// </summary>
        /// <param name="key">资源键名</param>
        /// <param name="asset">资源对象</param>
        void RegisterAsset(string key, Object asset);
        
        /// <summary>
        /// 获取资源
        /// </summary>
        T GetAsset<T>(string key) where T : Object;
        
        /// <summary>
        /// 检查资源是否存在
        /// </summary>
        bool HasAsset(string key);
        
        /// <summary>
        /// 注销资源
        /// </summary>
        void UnregisterAsset(string key);
        
        /// <summary>
        /// 注销指定前缀的所有资源（用于卸载整个mod的资源）
        /// </summary>
        void UnregisterAssetsByPrefix(string prefix);
        
        /// <summary>
        /// 获取所有资源键名
        /// </summary>
        IEnumerable<string> GetAllKeys();
    }
    
    /// <summary>
    /// 默认资源注册表实现
    /// </summary>
    public class DefaultAssetRegistry : IAssetRegistry
    {
        private readonly Dictionary<string, Object> _assets = new();
        
        public void RegisterAsset(string key, Object asset)
        {
            if (asset == null)
            {
                Debug.LogWarning($"[ModSystem] Attempted to register null asset with key: {key}");
                return;
            }
            
            if (_assets.ContainsKey(key))
            {
                Debug.LogWarning($"[ModSystem] Overwriting existing asset: {key}");
            }
            
            _assets[key] = asset;
        }
        
        public T GetAsset<T>(string key) where T : Object
        {
            if (_assets.TryGetValue(key, out var asset))
            {
                return asset as T;
            }
            return null;
        }
        
        public bool HasAsset(string key)
        {
            return _assets.ContainsKey(key);
        }
        
        public void UnregisterAsset(string key)
        {
            _assets.Remove(key);
        }
        
        public void UnregisterAssetsByPrefix(string prefix)
        {
            var keysToRemove = new List<string>();
            foreach (var key in _assets.Keys)
            {
                if (key.StartsWith(prefix))
                {
                    keysToRemove.Add(key);
                }
            }
            
            foreach (var key in keysToRemove)
            {
                _assets.Remove(key);
            }
        }
        
        public IEnumerable<string> GetAllKeys()
        {
            return _assets.Keys;
        }
    }
}
