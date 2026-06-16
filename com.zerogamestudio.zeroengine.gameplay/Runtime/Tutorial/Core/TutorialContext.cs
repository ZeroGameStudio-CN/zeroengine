using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Tutorial
{
    /// <summary>
    /// 教程执行上下文 (v1.14.0+)
    /// 在教程运行期间共享数据
    /// </summary>
    public class TutorialContext
    {
        #region Properties

        /// <summary>当前教程序列</summary>
        public TutorialSequenceSO CurrentSequence { get; private set; }

        /// <summary>当前步骤</summary>
        public TutorialStep CurrentStep { get; private set; }

        /// <summary>当前步骤索引</summary>
        public int CurrentStepIndex { get; private set; }

        /// <summary>教程开始时间</summary>
        public float StartTime { get; private set; }

        /// <summary>当前步骤开始时间</summary>
        public float StepStartTime { get; private set; }

        /// <summary>步骤已运行时间</summary>
        public float StepElapsedTime => Time.time - StepStartTime;

        /// <summary>教程已运行时间</summary>
        public float TotalElapsedTime => Time.time - StartTime;

        /// <summary>是否已完成</summary>
        public bool IsCompleted { get; private set; }

        /// <summary>是否被跳过</summary>
        public bool IsSkipped { get; private set; }

        /// <summary>玩家 GameObject (如果可用)</summary>
        public GameObject Player { get; set; }

        #endregion

        #region Variables

        // 本地变量 (仅当前教程有效)
        private readonly Dictionary<string, object> _localVariables = new();

        /// <summary>
        /// 设置本地变量
        /// </summary>
        public void SetVariable(string key, object value)
        {
            _localVariables[key] = value;
        }

        /// <summary>
        /// 获取本地变量
        /// </summary>
        public T GetVariable<T>(string key, T defaultValue = default)
        {
            if (_localVariables.TryGetValue(key, out var value))
            {
                if (value is T typedValue)
                {
                    return typedValue;
                }

                // 尝试转换
                try
                {
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        /// <summary>
        /// 检查变量是否存在
        /// </summary>
        public bool HasVariable(string key)
        {
            return _localVariables.ContainsKey(key);
        }

        /// <summary>
        /// 移除变量
        /// </summary>
        public void RemoveVariable(string key)
        {
            _localVariables.Remove(key);
        }

        /// <summary>
        /// 清除所有本地变量
        /// </summary>
        public void ClearVariables()
        {
            _localVariables.Clear();
        }

        #endregion

        #region Target Tracking

        // 高亮目标缓存
        private readonly Dictionary<string, GameObject> _targetCache = new();

        /// <summary>
        /// 注册目标对象
        /// </summary>
        public void RegisterTarget(string id, GameObject target)
        {
            _targetCache[id] = target;
        }

        /// <summary>
        /// 获取目标对象
        /// </summary>
        public GameObject GetTarget(string id)
        {
            if (_targetCache.TryGetValue(id, out var target))
            {
                return target;
            }
            return null;
        }

        /// <summary>
        /// 通过路径查找 UI 目标
        /// </summary>
        public RectTransform FindUITarget(string path)
        {
            // 缓存查找
            if (_targetCache.TryGetValue(path, out var cached))
            {
                if (cached != null)
                {
                    return cached.GetComponent<RectTransform>();
                }
            }

            // 查找策略
            GameObject found = null;

            // 1. 直接名称查找
            found = GameObject.Find(path);

            // 2. 在 Canvas 下查找
            if (found == null)
            {
                var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
                foreach (var canvas in canvases)
                {
                    var transform = canvas.transform.Find(path);
                    if (transform != null)
                    {
                        found = transform.gameObject;
                        break;
                    }
                }
            }

            // 缓存结果
            if (found != null)
            {
                _targetCache[path] = found;
                return found.GetComponent<RectTransform>();
            }

            return null;
        }

        /// <summary>
        /// 清除目标缓存
        /// </summary>
        public void ClearTargetCache()
        {
            _targetCache.Clear();
        }

        #endregion

        #region Internal Methods

        internal void Initialize(TutorialSequenceSO sequence, GameObject player = null)
        {
            CurrentSequence = sequence;
            CurrentStep = null;
            CurrentStepIndex = -1;
            StartTime = Time.time;
            StepStartTime = Time.time;
            IsCompleted = false;
            IsSkipped = false;
            Player = player;

            _localVariables.Clear();
            _targetCache.Clear();
        }

        internal void SetCurrentStep(TutorialStep step, int index)
        {
            CurrentStep = step;
            CurrentStepIndex = index;
            StepStartTime = Time.time;
        }

        internal void MarkCompleted()
        {
            IsCompleted = true;
        }

        internal void MarkSkipped()
        {
            IsSkipped = true;
        }

        internal void Reset()
        {
            CurrentSequence = null;
            CurrentStep = null;
            CurrentStepIndex = -1;
            IsCompleted = false;
            IsSkipped = false;
            Player = null;
            _localVariables.Clear();
            _targetCache.Clear();
        }

        #endregion

        #region Utility

        /// <summary>
        /// 获取当前进度百分比 (0-1)
        /// </summary>
        public float GetProgress()
        {
            if (CurrentSequence == null || CurrentSequence.Steps == null || CurrentSequence.Steps.Count == 0)
            {
                return 0;
            }

            return (float)(CurrentStepIndex + 1) / CurrentSequence.Steps.Count;
        }

        /// <summary>
        /// 获取剩余步骤数
        /// </summary>
        public int GetRemainingSteps()
        {
            if (CurrentSequence == null || CurrentSequence.Steps == null)
            {
                return 0;
            }

            return Mathf.Max(0, CurrentSequence.Steps.Count - CurrentStepIndex - 1);
        }

        #endregion
    }
}
