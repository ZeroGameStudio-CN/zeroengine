using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_LOCALIZATION
using UnityEngine.Localization;
#endif

namespace ZeroEngine.Localization
{
    /// <summary>
    /// LocalizedString 扩展方法
    /// 提供安全获取和 Debug 模式支持
    ///
    /// 性能优化:
    /// - 格式化字符串缓存，避免重复 GC 分配
    /// - 非阻塞默认模式，避免帧卡顿
    /// - 参数化重载，避免 params 数组分配
    /// </summary>
    public static class LocalizedStringExtensions
    {
        #region Settings

        /// <summary>
        /// Debug 模式：启用时显示 [key] 而非翻译文本
        /// 用于开发时快速定位本地化 key
        /// </summary>
        public static bool DebugMode { get; set; }

        /// <summary>
        /// 缺失 key 的显示格式
        /// {0} = key 名称
        /// </summary>
        public static string MissingKeyFormat { get; set; } = "[{0}]";

        #endregion

        #region Cache - 格式化缓存 (性能优化)

        // 缓存格式化后的 key 字符串，避免重复 string.Format 分配
        private static readonly Dictionary<string, string> _formattedKeyCache = new(128);
        private static readonly object _cacheLock = new();

        /// <summary>
        /// 清除格式化缓存（语言切换时可调用）
        /// </summary>
        public static void ClearCache()
        {
            lock (_cacheLock)
            {
                _formattedKeyCache.Clear();
            }
        }

        private static string GetFormattedKey(string key)
        {
            // 快速路径：缓存命中
            if (_formattedKeyCache.TryGetValue(key, out string cached))
                return cached;

            // 慢速路径：格式化并缓存
            lock (_cacheLock)
            {
                if (!_formattedKeyCache.TryGetValue(key, out cached))
                {
                    cached = string.Format(MissingKeyFormat, key);
                    _formattedKeyCache[key] = cached;
                }
            }
            return cached;
        }

        #endregion

#if UNITY_LOCALIZATION

        /// <summary>
        /// 安全获取本地化字符串（非阻塞）
        /// - 空引用返回 [NULL]
        /// - Debug 模式返回 [key]
        /// - 未加载时返回 [key]（不阻塞）
        /// - 空翻译返回 [key]
        /// </summary>
        /// <param name="ls">LocalizedString 引用</param>
        /// <param name="allowBlocking">保留旧签名；当前实现始终不阻塞</param>
        /// <returns>本地化文本或 key 占位符</returns>
        public static string GetSafe(this LocalizedString ls, bool allowBlocking = false)
        {
            // 空引用检查
            if (ls == null || ls.IsEmpty)
                return "[NULL]";

            // 获取 key 名称
            string key = ls.TableEntryReference.Key;

            // Debug 模式：直接显示 key（使用缓存）
            if (DebugMode)
                return GetFormattedKey(key);

            // 获取翻译
            var op = ls.GetLocalizedStringAsync();

            // 非阻塞模式：未完成时返回 key
            if (!op.IsDone)
            {
                return GetFormattedKey(key);
            }

            string result = op.Result;
            return string.IsNullOrEmpty(result) ? GetFormattedKey(key) : result;
        }

        /// <summary>
        /// 安全获取本地化字符串（带 1 个参数，避免 params 分配）
        /// </summary>
        public static string GetSafe(this LocalizedString ls, object arg0)
        {
            return GetSafeWithArgs(ls, arg0);
        }

        /// <summary>
        /// 安全获取本地化字符串（带 2 个参数，避免 params 分配）
        /// </summary>
        public static string GetSafe(this LocalizedString ls, object arg0, object arg1)
        {
            return GetSafeWithArgs(ls, arg0, arg1);
        }

        /// <summary>
        /// 安全获取本地化字符串（带 3 个参数，避免 params 分配）
        /// </summary>
        public static string GetSafe(this LocalizedString ls, object arg0, object arg1, object arg2)
        {
            return GetSafeWithArgs(ls, arg0, arg1, arg2);
        }

        /// <summary>
        /// 安全获取本地化字符串（带多个参数，4+ 参数时使用）
        /// </summary>
        public static string GetSafeParams(this LocalizedString ls, params object[] args)
        {
            return GetSafeWithArgs(ls, args);
        }

        private static string GetSafeWithArgs(LocalizedString ls, params object[] args)
        {
            if (ls == null || ls.IsEmpty)
                return "[NULL]";

            string key = ls.TableEntryReference.Key;

            if (DebugMode)
                return GetFormattedKey(key);

            // 参数化版本也保持非阻塞，异步服务负责初始化后获取文本。
            var op = ls.GetLocalizedStringAsync(args);

            if (!op.IsDone)
            {
                return GetFormattedKey(key);
            }

            string result = op.Result;
            return string.IsNullOrEmpty(result) ? GetFormattedKey(key) : result;
        }

        /// <summary>
        /// 检查 LocalizedString 是否有效（非空且有引用）
        /// </summary>
        public static bool IsValid(this LocalizedString ls)
        {
            return ls != null && !ls.IsEmpty;
        }

        /// <summary>
        /// 获取 key 名称（用于日志或调试）
        /// </summary>
        public static string GetKey(this LocalizedString ls)
        {
            if (ls == null || ls.IsEmpty)
                return null;
            return ls.TableEntryReference.Key;
        }

        /// <summary>
        /// 获取表名（用于日志或调试）
        /// </summary>
        public static string GetTableName(this LocalizedString ls)
        {
            if (ls == null || ls.IsEmpty)
                return null;
            return ls.TableReference.TableCollectionName;
        }

#else
        // 无 Unity Localization 时的 Stub 实现
        // 保持 API 兼容，返回占位符

        public static string GetSafe(this object ls)
        {
            return "[NO_LOCALIZATION]";
        }

        public static string GetSafe(this object ls, params object[] args)
        {
            return "[NO_LOCALIZATION]";
        }

        public static bool IsValid(this object ls)
        {
            return false;
        }

        public static string GetKey(this object ls)
        {
            return null;
        }

        public static string GetTableName(this object ls)
        {
            return null;
        }
#endif
    }
}
