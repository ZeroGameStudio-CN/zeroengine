using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

#if MOONSHARP
using MoonSharp.Interpreter;
#endif

namespace ZeroEngine.ModSystem.Scripting
{
    /// <summary>
    /// Mod脚本管理器。
    /// 为每个mod创建隔离的Lua环境。
    /// </summary>
    public class ModScriptManager
    {
        private Dictionary<string, LuaScriptRunner> _modScripts = new();
        
        /// <summary>
        /// 为mod初始化脚本环境
        /// </summary>
        public void InitializeModScripts(LoadedMod mod)
        {
#if MOONSHARP
            var scriptsFolder = Path.Combine(mod.Manifest.RootPath, "scripts");
            if (!Directory.Exists(scriptsFolder)) return;
            
            var runner = new LuaScriptRunner();
            _modScripts[mod.Manifest.Id] = runner;
            
            // 设置mod信息
            runner.SetGlobal("MOD_ID", mod.Manifest.Id);
            runner.SetGlobal("MOD_NAME", mod.Manifest.Name);
            runner.SetGlobal("MOD_VERSION", mod.Manifest.Version);
            
            // 加载主脚本（如果存在）
            var mainScript = Path.Combine(scriptsFolder, "main.lua");
            if (File.Exists(mainScript))
            {
                runner.ExecuteFile(mainScript);
                
                // 调用初始化函数（如果定义）
                runner.Call("OnModLoad");
            }
            
            // 加载scripts目录下的所有脚本
            foreach (var script in Directory.GetFiles(scriptsFolder, "*.lua"))
            {
                if (script != mainScript)
                {
                    runner.ExecuteFile(script);
                }
            }
            
            Debug.Log($"[ModScriptManager] Initialized scripts for mod: {mod.Manifest.Id}");
#else
            Debug.LogWarning($"[ModScriptManager] Lua scripting is disabled (MoonSharp not installed).");
#endif
        }
        
        /// <summary>
        /// 卸载mod脚本
        /// </summary>
        public void UnloadModScripts(string modId)
        {
            if (_modScripts.TryGetValue(modId, out var runner))
            {
#if MOONSHARP
                // 调用卸载函数（如果定义）
                runner.Call("OnModUnload");
#endif
                _modScripts.Remove(modId);
            }
        }
        
        /// <summary>
        /// 触发所有mod的事件
        /// </summary>
        public void BroadcastEvent(string eventName, params object[] args)
        {
#if MOONSHARP
            foreach (var kvp in _modScripts)
            {
                try
                {
                    kvp.Value.Call(eventName, args);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ModScriptManager] Error in mod {kvp.Key} event {eventName}: {ex.Message}");
                }
            }
#endif
        }
        
        /// <summary>
        /// 调用特定mod的函数
        /// </summary>
        public object CallModFunction(string modId, string functionName, params object[] args)
        {
#if MOONSHARP
            if (_modScripts.TryGetValue(modId, out var runner))
            {
                return runner.Call(functionName, args);
            }
#endif
            return null;
        }
        
        /// <summary>
        /// 获取mod的脚本运行器
        /// </summary>
        public LuaScriptRunner GetModRunner(string modId)
        {
            _modScripts.TryGetValue(modId, out var runner);
            return runner;
        }
    }
}
