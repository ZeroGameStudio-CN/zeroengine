using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Core;
using ZeroEngine.Save;

namespace ZeroEngine.Tutorial
{
    /// <summary>
    /// 教程管理器 (v1.14.0+)
    /// 管理教程序列的执行、进度和存档
    /// </summary>
    public class TutorialManager : MonoSingleton<TutorialManager>, ISaveable
    {
        #region Serialized Fields

        [Header("Config")]
        [SerializeField]
        [Tooltip("教程配置")]
        private TutorialConfigSO _config;

        [SerializeField]
        [Tooltip("已注册的教程序列")]
        private List<TutorialSequenceSO> _registeredSequences = new();

        [Header("Settings")]
        [SerializeField]
        [Tooltip("启用教程系统")]
        private bool _enabled = true;

        [SerializeField]
        [Tooltip("启用调试日志")]
        private bool _debugMode = false;

        #endregion

        #region Private Fields

        // 已完成的教程
        private readonly HashSet<string> _completedSequences = new();

        // 已跳过的教程
        private readonly HashSet<string> _skippedSequences = new();

        // 已触发的标记 (用于 FirstTimeCondition)
        private readonly HashSet<string> _triggeredMarkers = new();

        // 全局变量
        private readonly Dictionary<string, object> _globalVariables = new();

        // 序列索引 (ID -> SO)
        private readonly Dictionary<string, TutorialSequenceSO> _sequenceIndex = new();

        // 当前上下文
        private readonly TutorialContext _context = new();

        // 当前运行的序列
        private TutorialSequenceSO _currentSequence;
        private int _currentStepIndex = -1;
        private bool _isRunning;
        private TutorialStep _enteredStep;
        private string _checkpointSequenceId, _checkpointStepId;
        private int _checkpointStepIndex;
        private bool _skippingStep;
        private uint _runVersion;
        private readonly List<TutorialStartRequest> _pendingStarts = new List<TutorialStartRequest>();
        private long _requestOrder;
#if ZEROENGINE_SAVE
        private SaveSlotManager _saveOwner;
#endif
        public string LastError { get; private set; }
        public event Action<TutorialSequenceSO, string> OnSequenceInterrupted;

        public void Configure(TutorialConfigSO config)
        {
            _config = config;
            _context.Config = config;
            _context.Manager = this;
            _context.UseUnscaledTime = !config || config.UseUnscaledTime;
            if (config) Enabled = config.EnableTutorials;
        }

        public void CancelCurrentSequence()
        {
            if (!_isRunning) return;
            var sequence = _currentSequence;
            StopCurrentSequence(false);
            OnSequenceInterrupted?.Invoke(sequence, null);
        }

        #endregion

        #region Properties

        /// <summary>是否启用</summary>
        public bool Enabled
        {
            get => _enabled;
            set { if (!value && _isRunning) CancelCurrentSequence(); _enabled = value; }
        }

        /// <summary>是否正在运行教程</summary>
        public bool IsRunning => _isRunning;

        /// <summary>当前教程序列</summary>
        public TutorialSequenceSO CurrentSequence => _currentSequence;

        /// <summary>当前步骤</summary>
        public TutorialStep CurrentStep => _currentSequence?.GetStep(_currentStepIndex);

        /// <summary>当前步骤索引</summary>
        public int CurrentStepIndex => _currentStepIndex;

        /// <summary>教程上下文</summary>
        public TutorialContext Context => _context;

        /// <summary>已完成的教程数量</summary>
        public int CompletedCount => _completedSequences.Count;

        #endregion

        #region Events

        /// <summary>教程序列开始</summary>
        public event Action<TutorialSequenceSO> OnSequenceStarted;

        /// <summary>教程序列完成</summary>
        public event Action<TutorialSequenceSO> OnSequenceCompleted;

        /// <summary>教程序列跳过</summary>
        public event Action<TutorialSequenceSO> OnSequenceSkipped;

        /// <summary>步骤开始</summary>
        public event Action<TutorialStep, int> OnStepStarted;

        /// <summary>步骤完成</summary>
        public event Action<TutorialStep, int> OnStepCompleted;

        /// <summary>步骤跳过</summary>
        public event Action<TutorialStep, int> OnStepSkipped;

        #endregion

        #region ISaveable

        public string SaveKey => "Tutorial";

        public object ExportSaveData()
        {
            return new TutorialSaveData
            {
                CompletedSequences = new List<string>(_completedSequences).ToArray(),
                SkippedSequences = new List<string>(_skippedSequences).ToArray(),
                TriggeredMarkers = new List<string>(_triggeredMarkers).ToArray(),
                CurrentSequenceId = _checkpointSequenceId,
                CurrentStepIndex = _checkpointStepIndex,
                CurrentStepId = _checkpointStepId,
                GlobalVariables = ExportGlobalVariablesInternal()
            };
        }

        public void ImportSaveData(object data)
        {
            if (data is TutorialSaveData saveData)
            {
                _completedSequences.Clear();
                if (saveData.CompletedSequences != null)
                {
                    foreach (var id in saveData.CompletedSequences)
                    {
                        _completedSequences.Add(id);
                    }
                }

                _skippedSequences.Clear();
                if (saveData.SkippedSequences != null)
                {
                    foreach (var id in saveData.SkippedSequences)
                    {
                        _skippedSequences.Add(id);
                    }
                }

                ImportGlobalVariables(saveData.GlobalVariables);
                _triggeredMarkers.Clear();
                foreach (var marker in saveData.TriggeredMarkers ?? Array.Empty<string>()) _triggeredMarkers.Add(marker);

                // 恢复进行中的教程
                if (!string.IsNullOrEmpty(saveData.CurrentSequenceId))
                {
                    var sequence = GetSequence(saveData.CurrentSequenceId);
                    if (sequence != null)
                    {
                        _checkpointSequenceId = saveData.CurrentSequenceId;
                        _checkpointStepId = saveData.CurrentStepId;
                        int stableIndex = string.IsNullOrEmpty(saveData.CurrentStepId) ? saveData.CurrentStepIndex : sequence.Steps.FindIndex(step => step?.StepId == saveData.CurrentStepId);
                        _checkpointStepIndex = Mathf.Max(0, stableIndex);
                        if (_enabled) StartSequence(sequence, _checkpointStepIndex);
                    }
                }
            }
        }

        public void ResetToDefault()
        {
            _checkpointSequenceId = _checkpointStepId = null; _checkpointStepIndex = 0;
            _completedSequences.Clear();
            _skippedSequences.Clear();
            _triggeredMarkers.Clear();
            _globalVariables.Clear();

            if (_isRunning)
            {
                StopCurrentSequence(false);
            }
        }

        private TutorialVariable[] ExportGlobalVariablesInternal()
        {
            var list = new List<TutorialVariable>();
            foreach (var kvp in _globalVariables)
            {
                list.Add(new TutorialVariable
                {
                    Key = kvp.Key,
                    Value = kvp.Value?.ToString() ?? "",
                    Type = GetVariableType(kvp.Value)
                });
            }
            return list.ToArray();
        }

        private void ImportGlobalVariables(TutorialVariable[] variables)
        {
            _globalVariables.Clear();
            if (variables == null) return;

            foreach (var v in variables)
            {
                object value = v.Type switch
                {
                    TutorialVariableType.Int => int.TryParse(v.Value, out int i) ? i : 0,
                    TutorialVariableType.Float => float.TryParse(v.Value, out float f) ? f : 0f,
                    TutorialVariableType.Bool => bool.TryParse(v.Value, out bool b) && b,
                    _ => v.Value
                };
                _globalVariables[v.Key] = value;
            }
        }

        private TutorialVariableType GetVariableType(object value)
        {
            return value switch
            {
                int => TutorialVariableType.Int,
                float => TutorialVariableType.Float,
                bool => TutorialVariableType.Bool,
                _ => TutorialVariableType.String
            };
        }

        #endregion

        #region Unity Lifecycle

        protected override void Awake()
        {
            base.Awake();
            Configure(_config);
            BuildSequenceIndex();
        }

        private void Start()
        {
            // 注册到存档系统
#if ZEROENGINE_SAVE
            _saveOwner = UnityEngine.Object.FindObjectOfType<SaveSlotManager>();
            _saveOwner?.RegisterSaveable(this);
#endif
        }

        private void Update()
        {
            Tick();
        }

        public void Tick()
        {
            if (!_enabled) return;
            ProcessRequests();
            if (!_isRunning) return;

            // 更新当前步骤
            UpdateCurrentStep();
        }

        private void OnDisable()
        {
            if (_isRunning) CancelCurrentSequence();
            foreach (var request in _pendingStarts) request.Dispose();
            _pendingStarts.Clear();
        }

        public TutorialStartRequest QueueSequence(TutorialSequenceSO sequence, Func<bool> valid = null, float lifetimeSeconds = 60)
        {
            if (!sequence || lifetimeSeconds <= 0) throw new ArgumentException("A queued tutorial needs a sequence and positive lifetime.");
            var request = new TutorialStartRequest { Sequence = sequence, IsValid = valid,
                Deadline = _context.Focus.Realtime() + lifetimeSeconds, Order = ++_requestOrder };
            if (!_enabled || _pendingStarts.Exists(item => item.State == TutorialRequestState.Pending && item.Sequence.SequenceId == sequence.SequenceId))
                request.State = TutorialRequestState.Rejected;
            else { RegisterSequence(sequence); _pendingStarts.Add(request); }
            return request;
        }

        private void ProcessRequests()
        {
            double now = _context.Focus.Realtime();
            foreach (var request in _pendingStarts)
            {
                if (request.State != TutorialRequestState.Pending) continue;
                if (!request.Sequence || (request.IsValid != null && !request.IsValid())) request.State = TutorialRequestState.Cancelled;
                else if (now >= request.Deadline) request.State = TutorialRequestState.Expired;
                else if ((!request.Sequence.Replayable && IsSequenceCompleted(request.Sequence.SequenceId)) || IsSequenceSkipped(request.Sequence.SequenceId))
                    request.State = TutorialRequestState.Rejected;
            }
            _pendingStarts.RemoveAll(item => item.State != TutorialRequestState.Pending);
            if (_isRunning) return;
            TutorialStartRequest best = null;
            foreach (var request in _pendingStarts)
                if (CheckPrerequisites(request.Sequence) && request.Sequence.CanStart(_context) &&
                    (best == null || request.Sequence.Priority > best.Sequence.Priority ||
                    (request.Sequence.Priority == best.Sequence.Priority && request.Order < best.Order))) best = request;
            if (best == null) return;
            _pendingStarts.Remove(best); best.State = TutorialRequestState.Started;
            StartSequence(best.Sequence);
        }

        protected override void OnDestroy()
        {
            StopCurrentSequence(false);
#if ZEROENGINE_SAVE
            if (_saveOwner) _saveOwner.UnregisterSaveable(this);
            _saveOwner = null;
#endif
            base.OnDestroy();
        }

        #endregion

        #region Sequence Management

        /// <summary>
        /// 开始教程序列
        /// </summary>
        public void StartSequence(TutorialSequenceSO sequence, int startStep = 0)
        {
            if (sequence == null)
            {
                LogWarning("Cannot start null sequence");
                return;
            }

            if (!_enabled)
            {
                LogDebug("Tutorial system is disabled");
                return;
            }

            // 已完成且不可重播
            if (!sequence.Validate(out var validationErrors) || startStep < 0 || startStep >= sequence.StepCount)
            { LastError = string.Join("\n", validationErrors) + " Invalid checkpoint index."; return; }

            if (IsSequenceCompleted(sequence.SequenceId) && !sequence.Replayable)
            {
                LogDebug($"Sequence {sequence.SequenceId} already completed");
                return;
            }

            // 停止当前教程
            if (_isRunning)
            {
                StopCurrentSequence(false);
            }

            LogDebug($"Starting sequence: {sequence.SequenceId}");

            _currentSequence = sequence;
            _currentStepIndex = startStep - 1; // 会在 AdvanceStep 中 +1
            _isRunning = true;
            _runVersion++;
            LastError = null;
            _context.Config = _config;
            _context.UseUnscaledTime = !_config || _config.UseUnscaledTime;

            // 初始化上下文
            _context.Initialize(sequence, FindPlayer());

            // 触发事件
            OnSequenceStarted?.Invoke(sequence);
            if (!_isRunning || _currentSequence != sequence) return;

            // 开始第一个步骤
            AdvanceStep();
        }

        /// <summary>
        /// 通过 ID 开始教程序列
        /// </summary>
        public void StartSequence(string sequenceId, int startStep = 0)
        {
            var sequence = GetSequence(sequenceId);
            if (sequence != null)
            {
                StartSequence(sequence, startStep);
            }
            else
            {
                LogWarning($"Sequence not found: {sequenceId}");
            }
        }

        /// <summary>
        /// 跳过当前步骤
        /// </summary>
        public void SkipCurrentStep()
        {
            if (!_isRunning || _currentSequence == null) return;

            var currentStep = CurrentStep;
            if (currentStep != null && !currentStep.CanSkip) return;
            if (_config && !_config.AllowSkipping) return;

            LogDebug($"Skipping step {_currentStepIndex}");

            // 触发步骤跳过
            _skippingStep = true;
            currentStep?.OnSkip(_context);
            OnStepSkipped?.Invoke(currentStep, _currentStepIndex);

            // 推进到下一步
            AdvanceStep();
        }

        /// <summary>
        /// 跳过当前教程序列
        /// </summary>
        public void SkipCurrentSequence()
        {
            if (!_isRunning || _currentSequence == null) return;
            if (!_currentSequence.Skippable) return;
            if (_config && !_config.AllowSkipping) return;

            LogDebug($"Skipping sequence: {_currentSequence.SequenceId}");

            _skippedSequences.Add(_currentSequence.SequenceId);
            _context.MarkSkipped();

            var sequence = _currentSequence;

            // 停止
            StopCurrentSequence(false);
            _checkpointSequenceId = _checkpointStepId = null; _checkpointStepIndex = 0;

            // 触发事件
            OnSequenceSkipped?.Invoke(sequence);
        }

        /// <summary>
        /// 完成当前步骤
        /// </summary>
        public void CompleteCurrentStep()
        {
            if (!_isRunning) return;
            AdvanceStep();
        }

        #endregion

        #region Step Execution

        private void UpdateCurrentStep()
        {
            var currentStep = CurrentStep;
            if (currentStep == null) return;

            try { currentStep.OnUpdate(_context); }
            catch (Exception exception) { _context.Fail(exception.Message); }
            if (!string.IsNullOrEmpty(_context.Failure) || _context.CancelRequested)
            {
                LastError = _context.Failure;
                var interrupted = _currentSequence;
                StopCurrentSequence(false);
                OnSequenceInterrupted?.Invoke(interrupted, LastError);
                return;
            }
            if (_context.StepSkipRequested) { SkipCurrentStep(); return; }
            if (!_isRunning || CurrentStep != currentStep) return;

            // 检查自动完成
            if (currentStep.SupportsAutomaticCompletion && currentStep.AutoCompleteDelay > 0 &&
                _context.StepElapsedTime >= currentStep.AutoCompleteDelay)
            {
                AdvanceStep();
                return;
            }

            // 检查步骤完成
            if (currentStep.IsCompleted(_context))
            {
                AdvanceStep();
            }
        }

        private void AdvanceStep()
        {
            uint version = _runVersion;
            int previousIndex = _currentStepIndex;
            // 退出当前步骤
            var previousStep = _enteredStep;
            _enteredStep = null;
            if (previousStep != null)
            {
                try { previousStep.OnExit(_context); }
                catch (Exception exception)
                {
                    LastError = exception.Message;
                    var interrupted = _currentSequence;
                    StopCurrentSequence(false);
                    OnSequenceInterrupted?.Invoke(interrupted, LastError);
                    return;
                }
                if (!_skippingStep) OnStepCompleted?.Invoke(previousStep, _currentStepIndex);
            }
            _skippingStep = false;
            if (!_isRunning || _runVersion != version || _currentStepIndex != previousIndex) return;

            // 推进索引
            _currentStepIndex++;

            // 检查是否完成
            if (_currentStepIndex >= _currentSequence.StepCount)
            {
                CompleteSequence();
                return;
            }

            // 进入新步骤
            var newStep = CurrentStep;
            if (newStep != null)
            {
                _context.SetCurrentStep(newStep, _currentStepIndex);
                _checkpointSequenceId = _currentSequence.SequenceId;
                _checkpointStepId = newStep.StepId;
                _checkpointStepIndex = _currentStepIndex;
                _enteredStep = newStep;
                try { newStep.OnEnter(_context); }
                catch (Exception exception)
                {
                    LastError = exception.Message;
                    var interrupted = _currentSequence;
                    StopCurrentSequence(false);
                    OnSequenceInterrupted?.Invoke(interrupted, LastError);
                    return;
                }
                OnStepStarted?.Invoke(newStep, _currentStepIndex);

                LogDebug($"Step {_currentStepIndex}: {newStep.StepType}");
            }
        }

        private void CompleteSequence()
        {
            if (_currentSequence == null) return;

            LogDebug($"Sequence completed: {_currentSequence.SequenceId}");

            var sequence = _currentSequence;

            if (sequence.CompletionRewards != null && sequence.CompletionRewards.Count > 0)
            {
                string error = "Completion rewards require a project reward service.";
                bool accepted = false;
                try { accepted = _context.Rewards != null && _context.Rewards.TryGrant(sequence.SequenceId, sequence.CompletionRewards, out error); }
                catch (Exception exception) { error = exception.Message; }
                if (!accepted)
                {
                    LastError = string.IsNullOrEmpty(error) ? "The project rejected tutorial rewards." : error;
                    StopCurrentSequence(false);
                    OnSequenceInterrupted?.Invoke(sequence, LastError);
                    return;
                }
            }
            _completedSequences.Add(sequence.SequenceId);
            _context.MarkCompleted();

            // 停止
            StopCurrentSequence(true);
            _checkpointSequenceId = _checkpointStepId = null; _checkpointStepIndex = 0;

            // 触发事件
            OnSequenceCompleted?.Invoke(sequence);

            // 自动触发下一个教程
            if (!_isRunning && !string.IsNullOrEmpty(sequence.NextSequenceId))
            {
                StartSequence(sequence.NextSequenceId);
            }
        }

        private void StopCurrentSequence(bool completed)
        {
            _runVersion++;
            var entered = _enteredStep;
            _enteredStep = null;
            _isRunning = false;
            try { entered?.OnExit(_context); }
            catch (Exception exception) { LastError = exception.Message; Debug.LogException(exception, this); }
            _currentSequence = null;
            _currentStepIndex = -1;
            _skippingStep = false;

            if (!completed)
            {
                _context.Reset();
            }
        }

        #endregion

        #region Query API

        /// <summary>
        /// 检查教程序列是否已完成
        /// </summary>
        public bool IsSequenceCompleted(string sequenceId)
        {
            return _completedSequences.Contains(sequenceId);
        }

        /// <summary>
        /// 检查教程序列是否已跳过
        /// </summary>
        public bool IsSequenceSkipped(string sequenceId)
        {
            return _skippedSequences.Contains(sequenceId);
        }

        /// <summary>
        /// 检查是否完成任一教程
        /// </summary>
        public bool HasCompletedAny(params string[] sequenceIds)
        {
            foreach (var id in sequenceIds)
            {
                if (IsSequenceCompleted(id)) return true;
            }
            return false;
        }

        /// <summary>
        /// 获取教程序列
        /// </summary>
        public TutorialSequenceSO GetSequence(string sequenceId)
        {
            if (_sequenceIndex.TryGetValue(sequenceId, out var sequence))
            {
                return sequence;
            }
            return null;
        }

        /// <summary>
        /// 获取所有已注册的教程序列
        /// </summary>
        public IReadOnlyList<TutorialSequenceSO> GetAllSequences()
        {
            return _registeredSequences;
        }

        /// <summary>
        /// 检查标记是否已触发
        /// </summary>
        public bool HasTriggered(string marker)
        {
            return _triggeredMarkers.Contains(marker);
        }

        /// <summary>
        /// 触发标记
        /// </summary>
        public void TriggerMarker(string marker)
        {
            _triggeredMarkers.Add(marker);
        }

        #endregion

        #region Variable API

        /// <summary>
        /// 设置全局变量
        /// </summary>
        public void SetGlobalVariable(string key, object value)
        {
            _globalVariables[key] = value;
        }

        /// <summary>
        /// 获取全局变量
        /// </summary>
        public T GetGlobalVariable<T>(string key, T defaultValue = default)
        {
            if (_globalVariables.TryGetValue(key, out var value))
            {
                if (value is T typedValue) return typedValue;
                try { return (T)Convert.ChangeType(value, typeof(T)); }
                catch { return defaultValue; }
            }
            return defaultValue;
        }

        #endregion

        #region Auto-Start

        /// <summary>
        /// 检查并自动启动符合条件的教程
        /// </summary>
        public void CheckAutoStart()
        {
            if (!_enabled || _isRunning) return;

            TutorialSequenceSO bestMatch = null;
            int highestPriority = int.MinValue;

            foreach (var sequence in _registeredSequences)
            {
                if (!sequence.AutoStart) continue;
                if (IsSequenceCompleted(sequence.SequenceId) && !sequence.Replayable) continue;
                if (IsSequenceSkipped(sequence.SequenceId)) continue;
                if (!CheckPrerequisites(sequence)) continue;
                if (!sequence.CanStart(_context)) continue;

                if (sequence.Priority > highestPriority)
                {
                    highestPriority = sequence.Priority;
                    bestMatch = sequence;
                }
            }

            if (bestMatch != null)
            {
                StartSequence(bestMatch);
            }
        }

        private bool CheckPrerequisites(TutorialSequenceSO sequence)
        {
            if (sequence.Prerequisites == null || sequence.Prerequisites.Length == 0)
            {
                return true;
            }

            foreach (var prereq in sequence.Prerequisites)
            {
                if (!IsSequenceCompleted(prereq))
                {
                    return false;
                }
            }

            return true;
        }

        #endregion

        #region Reset

        /// <summary>
        /// 重置进度
        /// </summary>
        public void ResetProgress(string sequenceId = null)
        {
            if (string.IsNullOrEmpty(sequenceId))
            {
                // 重置所有
                _completedSequences.Clear();
                _skippedSequences.Clear();
                _triggeredMarkers.Clear();
                _globalVariables.Clear();

                if (_isRunning)
                {
                    StopCurrentSequence(false);
                }
            }
            else
            {
                // 重置指定教程
                _completedSequences.Remove(sequenceId);
                _skippedSequences.Remove(sequenceId);

                if (_currentSequence?.SequenceId == sequenceId)
                {
                    StopCurrentSequence(false);
                }
            }
        }

        #endregion

        #region Helpers

        private void BuildSequenceIndex()
        {
            _sequenceIndex.Clear();
            foreach (var sequence in _registeredSequences)
            {
                if (sequence != null && !string.IsNullOrEmpty(sequence.SequenceId))
                {
                    _sequenceIndex[sequence.SequenceId] = sequence;
                }
            }
        }

        private GameObject FindPlayer()
        {
            // 尝试查找带 "Player" 标签的对象
            return GameObject.FindGameObjectWithTag("Player");
        }

        /// <summary>
        /// 注册教程序列
        /// </summary>
        public void RegisterSequence(TutorialSequenceSO sequence)
        {
            if (sequence && _sequenceIndex.TryGetValue(sequence.SequenceId, out var owner) && owner != sequence)
                throw new InvalidOperationException("A different tutorial already owns sequence ID: " + sequence.SequenceId);
            if (sequence != null && !_registeredSequences.Contains(sequence))
            {
                _registeredSequences.Add(sequence);
                if (!string.IsNullOrEmpty(sequence.SequenceId))
                {
                    _sequenceIndex[sequence.SequenceId] = sequence;
                }
            }
        }

        #endregion

        #region Debug

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void LogDebug(string message)
        {
            if (_debugMode)
            {
                Debug.Log($"[ZeroEngine.Tutorial] {message}");
            }
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[ZeroEngine.Tutorial] {message}");
        }

        #endregion
    }
}
