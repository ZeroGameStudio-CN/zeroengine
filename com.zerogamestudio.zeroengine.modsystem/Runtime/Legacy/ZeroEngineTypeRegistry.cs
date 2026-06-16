using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.AbilitySystem;
using ZeroEngine.BuffSystem;
using ZeroEngine.Inventory;

namespace ZeroEngine.ModSystem
{
    /// <summary>
    /// ZeroEngine内置类型注册表。
    /// 自动注册AbilitySystem、BuffSystem、Inventory等模块的类型。
    /// </summary>
    public class ZeroEngineTypeRegistry : ITypeRegistry
    {
        private readonly Dictionary<string, Type> _types = new();
        private readonly Dictionary<string, Func<object>> _factories = new();
        
        public ZeroEngineTypeRegistry()
        {
            RegisterBuiltinTypes();
        }
        
        /// <summary>
        /// 注册ZeroEngine内置类型
        /// </summary>
        private void RegisterBuiltinTypes()
        {
            // ============ AbilitySystem Types ============
            
            // Trigger Components
            RegisterType("ManualTriggerData", typeof(ManualTriggerData));
            RegisterType("IntervalTriggerData", typeof(IntervalTriggerData));
            RegisterType("OnHitTriggerData", typeof(OnHitTriggerData));
            
            // Effect Components
            RegisterType("DamageEffectData", typeof(DamageEffectData));
            RegisterType("HealEffectData", typeof(HealEffectData));
            RegisterType("SpawnProjectileEffectData", typeof(SpawnProjectileEffectData));
            RegisterType("ApplyBuffEffectData", typeof(ApplyBuffEffectData));
            
            // Condition Components
            RegisterType("CooldownConditionData", typeof(CooldownConditionData));
            RegisterType("ResourceConditionData", typeof(ResourceConditionData));
            
            // ============ BuffSystem Types ============
            RegisterScriptableObjectType("BuffData", typeof(BuffData));
            RegisterType("BuffStatModifierConfig", typeof(BuffStatModifierConfig));
            
            // ============ Inventory Types ============
            RegisterScriptableObjectType("InventoryItemSO", typeof(InventoryItemSO));
            
            // ============ AbilityDataSO ============
            RegisterScriptableObjectType("AbilityDataSO", typeof(AbilityDataSO));
            
            Debug.Log($"[ZeroEngineTypeRegistry] Registered {_types.Count} built-in types.");
        }
        
        /// <summary>
        /// 注册普通类型
        /// </summary>
        public void RegisterType(string typeName, Type type)
        {
            _types[typeName] = type;
            _factories[typeName] = () => Activator.CreateInstance(type);
        }
        
        /// <summary>
        /// 注册ScriptableObject类型
        /// </summary>
        public void RegisterScriptableObjectType(string typeName, Type type)
        {
            if (!typeof(ScriptableObject).IsAssignableFrom(type))
            {
                Debug.LogError($"[ZeroEngineTypeRegistry] {typeName} is not a ScriptableObject type.");
                return;
            }
            
            _types[typeName] = type;
            _factories[typeName] = () => ScriptableObject.CreateInstance(type);
        }
        
        /// <summary>
        /// 注册带自定义工厂的类型
        /// </summary>
        public void RegisterTypeWithFactory(string typeName, Type type, Func<object> factory)
        {
            _types[typeName] = type;
            _factories[typeName] = factory;
        }
        
        public void RegisterTypes(IEnumerable<KeyValuePair<string, Type>> types)
        {
            foreach (var kvp in types)
            {
                RegisterType(kvp.Key, kvp.Value);
            }
        }
        
        public object CreateInstance(string typeName)
        {
            if (_factories.TryGetValue(typeName, out var factory))
            {
                return factory();
            }
            return null;
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
