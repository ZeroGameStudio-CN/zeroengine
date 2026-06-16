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
    /// Lua脚本运行器。
    /// 需要安装MoonSharp包并添加MOONSHARP宏定义。
    /// </summary>
    public class LuaScriptRunner
    {
#if MOONSHARP
        private Script _script;
        private Dictionary<string, DynValue> _cachedScripts = new();
        
        public LuaScriptRunner()
        {
            // 安全模式：禁用危险操作
            _script = new Script(CoreModules.Preset_SoftSandbox);
            
            // 注册自定义类型
            RegisterTypes();
            
            // 注册全局函数
            RegisterGlobalFunctions();
        }
        
        /// <summary>
        /// 注册可供Lua访问的类型
        /// </summary>
        private void RegisterTypes()
        {
            // 注册Unity类型
            UserData.RegisterType<Vector2>();
            UserData.RegisterType<Vector3>();
            UserData.RegisterType<Color>();
            
            // 注册自定义类型（游戏项目可扩展）
            // UserData.RegisterType<YourGameType>();
        }
        
        /// <summary>
        /// 注册全局函数供Lua调用
        /// </summary>
        private void RegisterGlobalFunctions()
        {
            // Log函数
            _script.Globals["Log"] = (Action<string>)(msg => Debug.Log($"[Lua] {msg}"));
            _script.Globals["LogWarning"] = (Action<string>)(msg => Debug.LogWarning($"[Lua] {msg}"));
            _script.Globals["LogError"] = (Action<string>)(msg => Debug.LogError($"[Lua] {msg}"));
            
            // 数学函数
            _script.Globals["Random"] = (Func<float, float, float>)((min, max) => UnityEngine.Random.Range(min, max));
            _script.Globals["RandomInt"] = (Func<int, int, int>)((min, max) => UnityEngine.Random.Range(min, max));
            
            // 时间函数
            _script.Globals["Time"] = (Func<float>)(() => Time.time);
            _script.Globals["DeltaTime"] = (Func<float>)(() => Time.deltaTime);
        }
        
        /// <summary>
        /// 执行Lua脚本文件
        /// </summary>
        public DynValue ExecuteFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogError($"[LuaScriptRunner] Script file not found: {filePath}");
                return DynValue.Nil;
            }
            
            try
            {
                var code = File.ReadAllText(filePath);
                return Execute(code, filePath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LuaScriptRunner] Error executing {filePath}: {ex.Message}");
                return DynValue.Nil;
            }
        }
        
        /// <summary>
        /// 执行Lua代码
        /// </summary>
        public DynValue Execute(string code, string chunkName = "chunk")
        {
            try
            {
                return _script.DoString(code, null, chunkName);
            }
            catch (ScriptRuntimeException ex)
            {
                Debug.LogError($"[LuaScriptRunner] Runtime error: {ex.DecoratedMessage}");
                return DynValue.Nil;
            }
            catch (SyntaxErrorException ex)
            {
                Debug.LogError($"[LuaScriptRunner] Syntax error: {ex.DecoratedMessage}");
                return DynValue.Nil;
            }
        }
        
        /// <summary>
        /// 调用Lua全局函数
        /// </summary>
        public DynValue Call(string functionName, params object[] args)
        {
            try
            {
                var func = _script.Globals.Get(functionName);
                if (func.IsNil())
                {
                    Debug.LogWarning($"[LuaScriptRunner] Function not found: {functionName}");
                    return DynValue.Nil;
                }
                
                return _script.Call(func, args);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LuaScriptRunner] Error calling {functionName}: {ex.Message}");
                return DynValue.Nil;
            }
        }
        
        /// <summary>
        /// 设置全局变量
        /// </summary>
        public void SetGlobal(string name, object value)
        {
            _script.Globals[name] = value;
        }
        
        /// <summary>
        /// 获取全局变量
        /// </summary>
        public T GetGlobal<T>(string name)
        {
            var value = _script.Globals.Get(name);
            return value.ToObject<T>();
        }
        
        /// <summary>
        /// 注册C#回调函数供Lua调用
        /// </summary>
        public void RegisterCallback(string name, Delegate callback)
        {
            _script.Globals[name] = callback;
        }
        
        /// <summary>
        /// 创建新的Lua表
        /// </summary>
        public Table CreateTable()
        {
            return new Table(_script);
        }
#else
        public LuaScriptRunner()
        {
            Debug.LogWarning("[LuaScriptRunner] MoonSharp is not installed. " +
                           "Install MoonSharp package and add MOONSHARP to scripting define symbols.");
        }
        
        public object ExecuteFile(string filePath)
        {
            Debug.LogWarning("[LuaScriptRunner] MoonSharp is not installed.");
            return null;
        }
        
        public object Execute(string code, string chunkName = "chunk")
        {
            Debug.LogWarning("[LuaScriptRunner] MoonSharp is not installed.");
            return null;
        }
        
        public object Call(string functionName, params object[] args)
        {
            Debug.LogWarning("[LuaScriptRunner] MoonSharp is not installed.");
            return null;
        }
        
        public void SetGlobal(string name, object value)
        {
            Debug.LogWarning("[LuaScriptRunner] MoonSharp is not installed.");
        }
        
        public T GetGlobal<T>(string name)
        {
            Debug.LogWarning("[LuaScriptRunner] MoonSharp is not installed.");
            return default;
        }
        
        public void RegisterCallback(string name, Delegate callback)
        {
            Debug.LogWarning("[LuaScriptRunner] MoonSharp is not installed.");
        }
#endif
    }
}
