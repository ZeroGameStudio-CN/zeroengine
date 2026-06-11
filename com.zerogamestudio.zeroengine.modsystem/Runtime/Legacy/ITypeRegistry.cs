using System;
using System.Collections.Generic;

namespace ZeroEngine.ModSystem
{
    /// <summary>
    /// 类型注册表接口。
    /// 游戏项目实现此接口，注册可被mod使用的类型。
    /// 这解决了LLS中硬编码Factory的问题。
    /// </summary>
    public interface ITypeRegistry
    {
        /// <summary>
        /// 根据类型名创建实例
        /// </summary>
        /// <param name="typeName">类型名称，如 "OnCheckTriggerData"</param>
        /// <returns>创建的实例，如果类型未注册则返回null</returns>
        object CreateInstance(string typeName);
        
        /// <summary>
        /// 注册类型（游戏启动时调用）
        /// </summary>
        /// <param name="typeName">类型名称</param>
        /// <param name="type">类型</param>
        void RegisterType(string typeName, Type type);
        
        /// <summary>
        /// 批量注册类型
        /// </summary>
        void RegisterTypes(IEnumerable<KeyValuePair<string, Type>> types);
        
        /// <summary>
        /// 检查类型是否已注册
        /// </summary>
        bool HasType(string typeName);
        
        /// <summary>
        /// 获取所有已注册类型名（用于编辑器/工具）
        /// </summary>
        IEnumerable<string> GetRegisteredTypeNames();
        
        /// <summary>
        /// 获取类型（不创建实例）
        /// </summary>
        Type GetType(string typeName);
    }
    
    /// <summary>
    /// 默认类型注册表实现
    /// </summary>
    public class DefaultTypeRegistry : ITypeRegistry
    {
        private readonly Dictionary<string, Type> _types = new();
        
        public object CreateInstance(string typeName)
        {
            if (_types.TryGetValue(typeName, out var type))
            {
                return Activator.CreateInstance(type);
            }
            return null;
        }
        
        public void RegisterType(string typeName, Type type)
        {
            _types[typeName] = type;
        }
        
        public void RegisterTypes(IEnumerable<KeyValuePair<string, Type>> types)
        {
            foreach (var kvp in types)
            {
                _types[kvp.Key] = kvp.Value;
            }
        }
        
        public bool HasType(string typeName)
        {
            return _types.ContainsKey(typeName);
        }
        
        public IEnumerable<string> GetRegisteredTypeNames()
        {
            return _types.Keys;
        }
        
        public Type GetType(string typeName)
        {
            _types.TryGetValue(typeName, out var type);
            return type;
        }
    }
}
