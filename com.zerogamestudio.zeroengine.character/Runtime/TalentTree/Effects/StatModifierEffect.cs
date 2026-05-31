using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.StatSystem;

namespace ZeroEngine.TalentTree
{
    /// <summary>
    /// 属性修改效果
    /// 用于给角色添加属性加成
    /// </summary>
    [Serializable]
    public class StatModifierEffect : TalentEffect
    {
        [Header("属性配置")]
        [Tooltip("属性 ID")]
        public StatId StatId;

        [Tooltip("修饰类型")]
        public StatModType ModType = StatModType.Flat;

        [Tooltip("每级增加的值")]
        public float ValuePerLevel = 1f;

        [Tooltip("基础值（0级时的值）")]
        public float BaseValue = 0f;

        // 追踪已应用的修饰器
        private StatModifier _appliedModifier;
        private StatController _cachedController;

        public override void Apply(GameObject owner, int level)
        {
            if (owner == null) return;

            var controller = owner.GetComponent<StatController>();
            if (controller == null)
            {
                Debug.LogWarning($"[TalentTree] StatModifierEffect: Owner {owner.name} 没有 StatController");
                return;
            }

            // 先移除旧的
            Remove(owner);

            // 计算值
            float value = BaseValue + ValuePerLevel * level;

            // 创建并应用修饰器
            _appliedModifier = new StatModifier(value, ModType, (int)ModType, this);
            _cachedController = controller;
            controller.AddModifier(StatId, _appliedModifier);
        }

        public override void Remove(GameObject owner)
        {
            if (_appliedModifier == null || _cachedController == null) return;

            _cachedController.RemoveModifier(StatId, _appliedModifier);
            _appliedModifier = null;
            _cachedController = null;
        }

        public override string GetDescription(int level)
        {
            float value = BaseValue + ValuePerLevel * level;
            string sign = value >= 0 ? "+" : "";
            string suffix = ModType == StatModType.Flat ? "" : "%";
            return $"{StatId} {sign}{value}{suffix}";
        }
    }

    /// <summary>
    /// 多属性修改效果
    /// 一次添加多个属性修饰
    /// </summary>
    [Serializable]
    public class MultiStatModifierEffect : TalentEffect
    {
        [Serializable]
        public class StatEntry
        {
            public StatId StatId;
            public StatModType ModType = StatModType.Flat;
            public float ValuePerLevel = 1f;
            public float BaseValue = 0f;

            public float GetValue(int level) => BaseValue + ValuePerLevel * level;
        }

        [Header("属性列表")]
        public List<StatEntry> Stats = new List<StatEntry>();

        private readonly List<(StatController Controller, StatId Id, StatModifier Modifier)> _appliedModifiers =
            new List<(StatController Controller, StatId Id, StatModifier Modifier)>();

        public override void Apply(GameObject owner, int level)
        {
            if (owner == null) return;

            var controller = owner.GetComponent<StatController>();
            if (controller == null) return;

            Remove(owner);

            foreach (var entry in Stats)
            {
                float value = entry.GetValue(level);
                var modifier = new StatModifier(value, entry.ModType, (int)entry.ModType, this);
                controller.AddModifier(entry.StatId, modifier);
                _appliedModifiers.Add((controller, entry.StatId, modifier));
            }
        }

        public override void Remove(GameObject owner)
        {
            foreach (var (controller, id, modifier) in _appliedModifiers)
            {
                controller?.RemoveModifier(id, modifier);
            }
            _appliedModifiers.Clear();
        }

        public override string GetDescription(int level)
        {
            var parts = new List<string>();
            foreach (var entry in Stats)
            {
                float value = entry.GetValue(level);
                string sign = value >= 0 ? "+" : "";
                string suffix = entry.ModType == StatModType.Flat ? "" : "%";
                parts.Add($"{entry.StatId} {sign}{value}{suffix}");
            }
            return string.Join(", ", parts);
        }
    }
}
