using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.AI.UtilityAI
{
    /// <summary>
    /// 效用 AI 大脑
    /// 基于效用理论的决策系统
    /// </summary>
    public class UtilityBrain : MonoBehaviour, IAIBrain
    {
        #region Serialized Fields

        [Header("Brain Settings")]
        [SerializeField] private string _brainName = "UtilityBrain";
        [SerializeField] private float _reevaluationInterval = 0.5f;
        [SerializeField] private float _minScoreThreshold = 0.1f;
        [SerializeField] private bool _allowInterruption = true;

        [Header("Score Settings")]
        [SerializeField] private float _inertiaBonus = 0.1f; // 当前行动的惯性加分
        [SerializeField] private float _hysteresis = 0.05f;  // 切换阈值 (防止频繁切换)

        [Header("Debug")]
        [SerializeField] private bool _enableDebug = false;

        [Header("Actions")]
        [SerializeReference]
        private List<UtilityAction> _actions = new();

        #endregion

        #region Runtime State

        private AIContext _context;
        private UtilityAction _currentAction;
        private float _lastEvaluationTime;
        private bool _isActive;
        private bool _forceReevaluate;

        #endregion

        #region Properties

        /// <summary>大脑名称</summary>
        public string BrainName => _brainName;

        /// <summary>是否激活</summary>
        public bool IsActive
        {
            get => _isActive;
            set => _isActive = value;
        }

        /// <summary>当前行动名称</summary>
        public string CurrentActionName => _currentAction?.Name ?? "None";

        /// <summary>当前行动</summary>
        public UtilityAction CurrentAction => _currentAction;

        /// <summary>所有行动</summary>
        public IReadOnlyList<UtilityAction> Actions => _actions;

        /// <summary>重评估间隔</summary>
        public float ReevaluationInterval
        {
            get => _reevaluationInterval;
            set => _reevaluationInterval = Mathf.Max(0.1f, value);
        }

        #endregion

        #region Events

        /// <summary>行动变更事件</summary>
        public event Action<UtilityAction, UtilityAction> OnActionChanged;

        /// <summary>评估完成事件</summary>
        public event Action<UtilityAction, float> OnEvaluationComplete;

        #endregion

        #region IAIBrain Implementation

        public void Initialize(AIContext context)
        {
            _context = context;
            _lastEvaluationTime = _context.Now;

            if (_enableDebug)
            {
                Debug.Log($"[UtilityBrain] Initialized with {_actions.Count} actions");
            }
        }

        public void Tick(float deltaTime)
        {
            if (!_isActive || _context == null) return;

            // 检查是否需要重新评估
            bool shouldReevaluate = _forceReevaluate ||
                                    _context.Now - _lastEvaluationTime >= _reevaluationInterval ||
                                    _currentAction == null;

            if (shouldReevaluate)
            {
                Evaluate();
                _forceReevaluate = false;
            }

            // 更新当前行动
            if (_currentAction != null)
            {
                var result = _currentAction.Update(_context, deltaTime);

                if (result.IsComplete)
                {
                    if (_enableDebug)
                    {
                        Debug.Log($"[UtilityBrain] Action '{_currentAction.Name}' completed: {result.State}");
                    }

                    _currentAction.Reset();
                    _currentAction = null;
                }
            }
        }

        public void ForceReevaluate()
        {
            _forceReevaluate = true;
        }

        public void StopCurrentAction()
        {
            if (_currentAction != null)
            {
                _currentAction.Stop(_context);
                _currentAction = null;
            }
        }

        public void Reset()
        {
            StopCurrentAction();

            foreach (var action in _actions)
            {
                action?.Reset();
            }

            _lastEvaluationTime = 0f;
            _forceReevaluate = false;
        }

        #endregion

        #region Evaluation

        /// <summary>
        /// 评估所有行动并选择最佳
        /// </summary>
        private void Evaluate()
        {
            _lastEvaluationTime = _context.Now;

            if (_actions.Count == 0)
            {
                return;
            }

            UtilityAction bestAction = null;
            float bestScore = _minScoreThreshold;

            // 当前行动的分数 (用于惯性和滞后)
            float currentActionScore = 0f;
            if (_currentAction != null)
            {
                currentActionScore = _currentAction.LastScore;
            }

            // 评估所有行动
            foreach (var action in _actions)
            {
                if (action == null || !action.IsEnabled) continue;
                if (!action.CanExecute(_context)) continue;

                float score = action.CalculateScore(_context);

                // 应用惯性加分 (当前行动额外加分)
                if (action == _currentAction && _inertiaBonus > 0f)
                {
                    score += _inertiaBonus;
                }

                if (_enableDebug)
                {
                    Debug.Log($"[UtilityBrain] {action.Name}: {score:F3}");
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestAction = action;
                }
            }

            // 应用滞后 (需要超过当前行动一定阈值才切换)
            if (_currentAction != null && bestAction != _currentAction)
            {
                if (bestScore < currentActionScore + _hysteresis)
                {
                    bestAction = _currentAction;
                }
            }

            // 切换行动
            if (bestAction != _currentAction)
            {
                SwitchAction(bestAction);
            }

            OnEvaluationComplete?.Invoke(bestAction, bestScore);
        }

        /// <summary>
        /// 切换到新行动
        /// </summary>
        private void SwitchAction(UtilityAction newAction)
        {
            // 检查是否允许中断
            if (_currentAction != null && !_allowInterruption &&
                _currentAction.State == AIActionState.Running)
            {
                return;
            }

            var previousAction = _currentAction;

            // 停止当前行动
            if (_currentAction != null)
            {
                _currentAction.Stop(_context);
            }

            _currentAction = newAction;

            // 开始新行动
            if (_currentAction != null)
            {
                _currentAction.Start(_context);
            }

            OnActionChanged?.Invoke(previousAction, _currentAction);

            if (_enableDebug)
            {
                Debug.Log($"[UtilityBrain] Switched: {previousAction?.Name ?? "None"} -> {_currentAction?.Name ?? "None"}");
            }
        }

        #endregion

        #region Action Management

        /// <summary>
        /// 添加行动
        /// </summary>
        public void AddAction(UtilityAction action)
        {
            if (action != null && !_actions.Contains(action))
            {
                _actions.Add(action);
            }
        }

        /// <summary>
        /// 移除行动
        /// </summary>
        public bool RemoveAction(UtilityAction action)
        {
            if (action == _currentAction)
            {
                StopCurrentAction();
            }
            return _actions.Remove(action);
        }

        /// <summary>
        /// 通过名称获取行动
        /// </summary>
        public UtilityAction GetAction(string name)
        {
            foreach (var action in _actions)
            {
                if (action?.Name == name)
                {
                    return action;
                }
            }
            return null;
        }

        /// <summary>
        /// 通过类型获取行动
        /// </summary>
        public T GetAction<T>() where T : UtilityAction
        {
            foreach (var action in _actions)
            {
                if (action is T typedAction)
                {
                    return typedAction;
                }
            }
            return null;
        }

        /// <summary>
        /// 清除所有行动
        /// </summary>
        public void ClearActions()
        {
            StopCurrentAction();
            _actions.Clear();
        }

        #endregion

        #region Debug

        /// <summary>
        /// 获取所有行动的评分详情
        /// </summary>
        public Dictionary<string, ActionScoreInfo> GetAllScores()
        {
            var scores = new Dictionary<string, ActionScoreInfo>();

            foreach (var action in _actions)
            {
                if (action == null) continue;

                scores[action.Name] = new ActionScoreInfo
                {
                    Score = action.LastScore,
                    IsCurrentAction = action == _currentAction,
                    State = action.State,
                    ConsiderationScores = action.GetDetailedScores()
                };
            }

            return scores;
        }

        #endregion
    }

    /// <summary>
    /// 行动评分信息 (用于调试)
    /// </summary>
    public struct ActionScoreInfo
    {
        public float Score;
        public bool IsCurrentAction;
        public AIActionState State;
        public Dictionary<string, float> ConsiderationScores;
    }
}
