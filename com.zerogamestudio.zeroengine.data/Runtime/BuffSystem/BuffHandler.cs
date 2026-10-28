using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.StatSystem;
using ZeroEngine.BuffSystem.Runtime;

namespace ZeroEngine.BuffSystem
{
    /// <summary>
    /// Buff 运行时处理器，管理单个 Buff 实例的状态。
    /// 由 BuffReceiver 创建和管理，不应手动实例化。
    /// </summary>
    public class BuffHandler
    {
        /// <summary>
        /// Buff 的配置数据
        /// </summary>
        public BuffData Data { get; private set; }

        /// <summary>
        /// 当前层数
        /// </summary>
        public int CurrentStacks => _stacks.TotalStacks;

        /// <summary>
        /// 剩余持续时间（秒）
        /// </summary>
        public float RemainingTime { get; private set; }

        /// <summary>
        /// Buff 是否已过期
        /// </summary>
        public bool IsExpired => _lifecycle.IsReleased;

        private readonly BuffStackState _stacks = new BuffStackState();
        private readonly BuffLifecycle _lifecycle = new BuffLifecycle();
        private float _tickTimer;
        private StatController _targetStats;
        
        // Refactored: Track type with modifier to allow removal
        private struct AppliedModifier
        {
            public StatType Type;
            public StatModifier Modifier;
        }
        private List<AppliedModifier> _appliedModifiers = new List<AppliedModifier>();

        /// <summary>
        /// Buff 过期时触发
        /// </summary>
        public event Action<BuffHandler> OnExpired;

        /// <summary>
        /// Buff Tick 时触发（用于周期性效果如 DOT/HOT）
        /// </summary>
        public event Action<BuffHandler> OnTick;

        /// <summary>
        /// 创建 Buff 处理器
        /// </summary>
        /// <param name="data">Buff 配置数据</param>
        /// <param name="targetStats">目标属性控制器（用于应用修饰器）</param>
        public BuffHandler(BuffData data, StatController targetStats)
        {
            Data = data;
            _targetStats = targetStats;
            RemainingTime = data.Duration;
        }

        /// <summary>
        /// 添加层数
        /// </summary>
        /// <param name="count">要添加的层数</param>
        public void AddStacks(int count)
        {
            if (IsExpired) return;

            var oldStacks = CurrentStacks;
            _stacks.SetSourceStackCount(string.Empty, Math.Min(unchecked(oldStacks + count), Data.MaxStacks));

            if (Data.RefreshOnAddStack) RefreshDuration();

            UpdateStatModifiers(oldStacks, CurrentStacks);
        }

        /// <summary>
        /// 移除层数
        /// </summary>
        /// <param name="count">要移除的层数</param>
        public void RemoveStacks(int count)
        {
            if (IsExpired) return;

            var oldStacks = CurrentStacks;
            _stacks.SetSourceStackCount(string.Empty, unchecked(oldStacks - count));

            if (Data.RefreshOnRemoveStack) RefreshDuration();

            if (CurrentStacks <= 0)
            {
                Expire();
            }
            else
            {
                UpdateStatModifiers(oldStacks, CurrentStacks);
            }
        }

        /// <summary>
        /// 每帧更新，由 BuffReceiver 调用
        /// </summary>
        /// <param name="deltaTime">帧间隔时间</param>
        public void Tick(float deltaTime)
        {
            if (IsExpired) return;

            if (Data.Duration > 0)
            {
                RemainingTime = BuffClock.AdvanceDuration(RemainingTime, deltaTime);
                if (RemainingTime <= 0)
                {
                    HandleExpire();
                }
            }
            
            if (BuffClock.TryAdvanceTick(deltaTime, ref _tickTimer, Data.TickInterval))
                OnTick?.Invoke(this);
        }

        /// <summary>
        /// 刷新持续时间 (v1.2.0+)
        /// </summary>
        public void RefreshDuration()
        {
            RemainingTime = Data.Duration;
        }

        /// <summary>
        /// 重置层数为 0（但不过期，用于 Replace 模式）(v1.2.0+)
        /// </summary>
        public void ResetStacks()
        {
            if (IsExpired) return;
            UpdateStatModifiers(CurrentStacks, 0);
            _stacks.Clear();
        }

        /// <summary>
        /// 强制过期 (v1.2.0+)
        /// </summary>
        public void ForceExpire()
        {
            Expire();
        }

        private void HandleExpire()
        {
            if (Data.ExpireMode == BuffExpireMode.RemoveAllStacks)
            {
                Expire();
            }
            else if (Data.ExpireMode == BuffExpireMode.RemoveOneStack)
            {
                RemoveStacks(1);
                if (!IsExpired) RefreshDuration();
            }
        }

        private void Expire()
        {
            if (IsExpired) return;
            _lifecycle.Release();
            _stacks.Clear();
            
            ClearModifiers();
            
            OnExpired?.Invoke(this);
        }

        private void UpdateStatModifiers(int oldStacks, int newStacks)
        {
            if (_targetStats == null) return;
            if (Data.StatModifiers == null || Data.StatModifiers.Count == 0) return;

            int delta = newStacks - oldStacks;
            if (delta == 0) return;

            if (delta > 0)
            {
                // Add modifiers for each new stack
                for (int i = 0; i < delta; i++)
                {
                    foreach (var modConfig in Data.StatModifiers)
                    {
                        var mod = new StatModifier(modConfig.Value, modConfig.ModType, (int)modConfig.ModType, this);
                        _targetStats.AddModifier(modConfig.StatType, mod);
                        
                        _appliedModifiers.Add(new AppliedModifier 
                        { 
                            Type = modConfig.StatType, 
                            Modifier = mod 
                        });
                    }
                }
            }
            else
            {
                // Remove modifiers for each removed stack
                // We remove from the end to be efficient
                int removeStacks = -delta;
                int modifiersPerStack = Data.StatModifiers.Count;
                int totalToRemove = removeStacks * modifiersPerStack;
                
                for (int i = 0; i < totalToRemove; i++)
                {
                    if (_appliedModifiers.Count > 0)
                    {
                        var lastIndex = _appliedModifiers.Count - 1;
                        var entry = _appliedModifiers[lastIndex];
                        
                        _targetStats.RemoveModifier(entry.Type, entry.Modifier);
                        _appliedModifiers.RemoveAt(lastIndex);
                    }
                }
            }
        }

        /// <summary>
        /// 清除所有已应用的属性修饰器
        /// </summary>
        public void ClearModifiers()
        {
            if (_targetStats == null) return;

            foreach (var entry in _appliedModifiers)
            {
                _targetStats.RemoveModifier(entry.Type, entry.Modifier);
            }
            _appliedModifiers.Clear();
        }
    }
}
