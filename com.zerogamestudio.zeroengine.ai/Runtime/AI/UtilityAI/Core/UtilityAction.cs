using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.AI.UtilityAI
{
    /// <summary>
    /// 效用行动基类
    /// 包含考量因素列表和行动执行逻辑
    /// </summary>
    [Serializable]
    public abstract class UtilityAction
    {
        #region Serialized Fields

        [SerializeField] protected string _name = "";
        [SerializeField] protected string _description = "";
        [SerializeField] protected float _basePriority = 1f;
        [SerializeField] protected bool _isEnabled = true;
        [SerializeField] protected float _cooldown = 0f;

        [SerializeReference]
        protected List<Consideration> _considerations = new();

        #endregion

        #region Runtime State

        private float _lastExecuteTime = float.NegativeInfinity;
        private float _lastScore;
        private AIActionState _state = AIActionState.Idle;

        #endregion

        #region Properties

        /// <summary>行动名称</summary>
        public string Name
        {
            get => string.IsNullOrEmpty(_name) ? GetType().Name : _name;
            set => _name = value;
        }

        /// <summary>描述</summary>
        public string Description
        {
            get => _description;
            set => _description = value;
        }

        /// <summary>基础优先级</summary>
        public float BasePriority
        {
            get => _basePriority;
            set => _basePriority = Mathf.Max(0f, value);
        }

        /// <summary>是否启用</summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => _isEnabled = value;
        }

        /// <summary>考量因素列表</summary>
        public IReadOnlyList<Consideration> Considerations => _considerations;

        /// <summary>最后计算的评分</summary>
        public float LastScore => _lastScore;

        /// <summary>当前状态</summary>
        public AIActionState State => _state;

        /// <summary>是否在冷却中</summary>
        private AIContext timeContext;
        private float Now => timeContext != null ? timeContext.Now : Time.time;

        public bool IsOnCooldown => _cooldown > 0f && Now - _lastExecuteTime < _cooldown;

        /// <summary>剩余冷却时间</summary>
        public float RemainingCooldown => IsOnCooldown ? _cooldown - (Now - _lastExecuteTime) : 0f;

        #endregion

        #region Consideration Management

        /// <summary>
        /// 添加考量因素
        /// </summary>
        public void AddConsideration(Consideration consideration)
        {
            if (consideration != null && !_considerations.Contains(consideration))
            {
                _considerations.Add(consideration);
            }
        }

        /// <summary>
        /// 移除考量因素
        /// </summary>
        public bool RemoveConsideration(Consideration consideration)
        {
            return _considerations.Remove(consideration);
        }

        /// <summary>
        /// 清除所有考量因素
        /// </summary>
        public void ClearConsiderations()
        {
            _considerations.Clear();
        }

        #endregion

        #region Scoring

        /// <summary>
        /// 计算综合评分
        /// 使用乘法组合 (Compensated Multiplicative)
        /// </summary>
        public float CalculateScore(AIContext context)
        {
            timeContext = context;
            if (!_isEnabled || IsOnCooldown)
            {
                _lastScore = 0f;
                return 0f;
            }

            if (_considerations.Count == 0)
            {
                _lastScore = _basePriority;
                return _basePriority;
            }

            // 使用补偿乘法评分
            // 避免单个低分完全否决行动
            float score = _basePriority;
            float modificationFactor = 1f - (1f / _considerations.Count);

            foreach (var consideration in _considerations)
            {
                if (consideration == null || !consideration.IsEnabled) continue;

                float considerationScore = consideration.Evaluate(context);

                // 补偿乘法
                float makeUpValue = (1f - considerationScore) * modificationFactor;
                float finalValue = considerationScore + (makeUpValue * considerationScore);

                score *= finalValue;

                // 如果分数太低，提前退出
                if (score < 0.001f)
                {
                    _lastScore = 0f;
                    return 0f;
                }
            }

            _lastScore = score;
            return score;
        }

        /// <summary>
        /// 获取各考量因素的详细评分
        /// </summary>
        public Dictionary<string, float> GetDetailedScores()
        {
            var details = new Dictionary<string, float>();
            foreach (var consideration in _considerations)
            {
                if (consideration != null)
                {
                    details[consideration.Name] = consideration.LastScore;
                }
            }
            return details;
        }

        #endregion

        #region Execution

        /// <summary>
        /// 开始执行行动
        /// </summary>
        public void Start(AIContext context)
        {
            timeContext = context;
            if (_state == AIActionState.Running) return;

            _state = AIActionState.Running;
            context.ActionStartTime = context.Now;

            OnStart(context);
        }

        /// <summary>
        /// 更新行动执行
        /// </summary>
        public AIActionResult Update(AIContext context, float deltaTime)
        {
            if (_state != AIActionState.Running)
            {
                return new AIActionResult(_state);
            }

            var result = OnUpdate(context, deltaTime);

            if (result.IsComplete)
            {
                _state = result.State;
                _lastExecuteTime = context.Now;
                OnEnd(context, result);
            }

            return result;
        }

        /// <summary>
        /// 停止行动
        /// </summary>
        public void Stop(AIContext context)
        {
            if (_state != AIActionState.Running) return;

            _state = AIActionState.Cancelled;
            OnEnd(context, AIActionResult.Cancelled());
        }

        /// <summary>
        /// 重置行动状态
        /// </summary>
        public void Reset()
        {
            _state = AIActionState.Idle;
            _lastScore = 0f;
        }

        #endregion

        #region Virtual Methods

        /// <summary>
        /// 行动开始时调用
        /// </summary>
        protected virtual void OnStart(AIContext context) { }

        /// <summary>
        /// 行动更新时调用
        /// </summary>
        protected abstract AIActionResult OnUpdate(AIContext context, float deltaTime);

        /// <summary>
        /// 行动结束时调用
        /// </summary>
        protected virtual void OnEnd(AIContext context, AIActionResult result) { }

        /// <summary>
        /// 检查行动是否可以执行
        /// </summary>
        public virtual bool CanExecute(AIContext context)
        {
            return _isEnabled && !IsOnCooldown;
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return $"{Name}: Score={_lastScore:F3}, State={_state}";
        }

        #endregion
    }

    /// <summary>
    /// 简单行动 - 立即完成的行动
    /// </summary>
    [Serializable]
    public class SimpleAction : UtilityAction
    {
        private Action<AIContext> _action;

        public SimpleAction() { }

        public SimpleAction(string name, Action<AIContext> action)
        {
            _name = name;
            _action = action;
        }

        public void SetAction(Action<AIContext> action)
        {
            _action = action;
        }

        protected override AIActionResult OnUpdate(AIContext context, float deltaTime)
        {
            _action?.Invoke(context);
            return AIActionResult.Success();
        }
    }

    /// <summary>
    /// 持续行动 - 持续一段时间的行动
    /// </summary>
    [Serializable]
    public class DurationAction : UtilityAction
    {
        [SerializeField] private float _duration = 1f;

        public float Duration
        {
            get => _duration;
            set => _duration = Mathf.Max(0f, value);
        }

        public DurationAction() { }

        public DurationAction(string name, float duration)
        {
            _name = name;
            _duration = duration;
        }

        protected override AIActionResult OnUpdate(AIContext context, float deltaTime)
        {
            if (context.ActionDuration >= _duration)
            {
                return AIActionResult.Success();
            }

            return AIActionResult.Running();
        }
    }

    /// <summary>
    /// 条件行动 - 等待条件满足
    /// </summary>
    [Serializable]
    public class ConditionalAction : UtilityAction
    {
        [SerializeField] private float _timeout = 10f;

        private Func<AIContext, bool> _condition;

        public float Timeout
        {
            get => _timeout;
            set => _timeout = Mathf.Max(0f, value);
        }

        public ConditionalAction() { }

        public ConditionalAction(string name, Func<AIContext, bool> condition, float timeout = 10f)
        {
            _name = name;
            _condition = condition;
            _timeout = timeout;
        }

        public void SetCondition(Func<AIContext, bool> condition)
        {
            _condition = condition;
        }

        protected override AIActionResult OnUpdate(AIContext context, float deltaTime)
        {
            if (_condition?.Invoke(context) == true)
            {
                return AIActionResult.Success();
            }

            if (_timeout > 0f && context.ActionDuration >= _timeout)
            {
                return AIActionResult.Failed("Timeout");
            }

            return AIActionResult.Running();
        }
    }
}
