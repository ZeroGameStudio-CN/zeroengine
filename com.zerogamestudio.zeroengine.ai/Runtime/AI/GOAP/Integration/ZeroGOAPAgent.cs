using System;
using UnityEngine;
using CrashKonijn.Goap.Behaviours;
using CrashKonijn.Goap.Interfaces;
using CrashKonijn.Goap.Classes;
namespace ZeroEngine.AI.GOAP
{

    /// <summary>
    /// ZeroEngine GOAP Agent 包装器
    /// 将 crashkonijn/GOAP 集成到 ZeroEngine AI 系统
    /// </summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "ZeroEngine.AI.GOAP", "ZeroEngine.AI", "ZeroGOAPAgent")]
    public class ZeroGOAPAgent : MonoBehaviour, IAIBrain
    {
        #region Serialized Fields

        [Header("GOAP Settings")]
        [SerializeField] private AgentBehaviour _goapAgent;
        [SerializeField] private bool _autoInitialize = true;

        [Header("ZeroEngine Integration")]
        [SerializeField] private bool _syncBlackboard = true;
        [SerializeField] private float _blackboardSyncInterval = 0.1f;

        #endregion

        #region Runtime State

        private AIContext _context;
        private float _syncTimer;
        private bool _isActive;

        #endregion

        #region Properties

        public bool IsActive
        {
            get => _isActive;
            set
            {
                _isActive = value;
                if (_goapAgent != null)
                {
                    _goapAgent.enabled = value;
                }
            }
        }

        public string CurrentActionName
        {
            get
            {
                if (_goapAgent?.CurrentAction != null)
                {
                    return _goapAgent.CurrentAction.GetType().Name;
                }
                return "None";
            }
        }

        public AgentBehaviour GOAPAgent => _goapAgent;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_goapAgent == null)
            {
                _goapAgent = GetComponent<AgentBehaviour>();
            }
        }

        #endregion

        #region IAIBrain Implementation

        public void Initialize(AIContext context)
        {
            _context = context;

            if (_autoInitialize && _goapAgent != null)
            {
                // GOAP Agent 初始化由 GoapRunnerBehaviour 处理
            }
        }

        public void Tick(float deltaTime)
        {
            if (!_isActive || _context == null) return;

            // 同步黑板
            if (_syncBlackboard)
            {
                _syncTimer -= deltaTime;
                if (_syncTimer <= 0f)
                {
                    _syncTimer = _blackboardSyncInterval;
                    SyncBlackboard();
                }
            }
        }

        public void ForceReevaluate()
        {
            // crashkonijn/GOAP 会自动重新评估
        }

        public void StopCurrentAction()
        {
            // 由 GOAP 系统管理
        }

        public void Reset()
        {
            _syncTimer = 0f;
        }

        #endregion

        #region Blackboard Sync

        /// <summary>
        /// 同步 ZeroEngine 黑板到 GOAP
        /// </summary>
        private void SyncBlackboard()
        {
            if (_context?.Blackboard == null) return;

            // 同步常用值到 GOAP WorldData
            // 具体实现取决于项目需求
        }

        /// <summary>
        /// 从 ZeroEngine 黑板设置 GOAP 世界状态
        /// </summary>
        public void SetWorldStateFromBlackboard(string blackboardKey, string worldKey)
        {
            if (_context?.Blackboard == null) return;

            // 示例: 将黑板值传递给 GOAP
            // 需要根据实际的 GOAP 配置实现
        }

        #endregion
    }

    /// <summary>
    /// GOAP 目标基类 - 与 ZeroEngine 集成
    /// </summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "ZeroEngine.AI.GOAP", "ZeroEngine.AI", "ZeroGOAPGoal")]
    public abstract class ZeroGOAPGoal : GoalBase
    {
        protected AIContext Context { get; private set; }

        public void SetContext(AIContext context)
        {
            Context = context;
        }
    }

    /// <summary>
    /// GOAP 行动基类 - 与 ZeroEngine 集成
    /// </summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "ZeroEngine.AI.GOAP", "ZeroEngine.AI", "ZeroGOAPAction")]
    public abstract class ZeroGOAPAction : ActionBase
    {
        protected AIContext Context { get; private set; }

        public void SetContext(AIContext context)
        {
            Context = context;
        }

        /// <summary>
        /// 从黑板获取值
        /// </summary>
        protected T GetBlackboardValue<T>(string key, T defaultValue = default)
        {
            return Context?.Blackboard?.Get(key, defaultValue) ?? defaultValue;
        }

        /// <summary>
        /// 设置黑板值
        /// </summary>
        protected void SetBlackboardValue<T>(string key, T value)
        {
            Context?.Blackboard?.Set(key, value);
        }
    }


}
