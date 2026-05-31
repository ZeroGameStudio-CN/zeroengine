using System;
using System.Collections.Generic;
using UnityEngine;

#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace ZeroEngine.AbilitySystem
{
#if ODIN_INSPECTOR
    [CreateAssetMenu(fileName = "NewAbility", menuName = "ZeroEngine/Ability System/Ability Data")]
    [Obsolete("Legacy ability data. Use AbilityDefinition with AbilityExecutor and IAbilityRuntimeServices for new TCE content.")]
    public class AbilityDataSO : SerializedScriptableObject
    {
        [Title("Basic Info")]
        public string AbilityName;
        [TextArea] public string Description;
        public Sprite Icon;

        [Title("Casting", "Define cast times and interruption")]
        [Tooltip("Time before the ability activates (can be interrupted)")]
        public float CastTime = 0f;

        [Tooltip("Time after cast before another action")]
        public float RecoveryTime = 0f;

        [Tooltip("Can this ability be interrupted during cast?")]
        public bool Interruptible = true;

        [Tooltip("Base cooldown time in seconds")]
        public float BaseCooldown = 1f;

        [Title("Leveling", "Scaling per level")]
        [Tooltip("Maximum level for this ability")]
        public int MaxLevel = 5;

        [Tooltip("Damage/effect multiplier per level (additive)")]
        public float EffectScalePerLevel = 0.1f;

        [Tooltip("Cooldown reduction per level (seconds)")]
        public float CooldownReductionPerLevel = 0.1f;

        [Title("Logic", "Define usage rules and effects")]

        [ListDrawerSettings(ShowFoldout = true)]
        [Searchable]
        [SerializeReference]
        public List<TriggerComponentData> Triggers = new List<TriggerComponentData>();

        [ListDrawerSettings(ShowFoldout = true)]
        [Searchable]
        [SerializeReference]
        public List<ConditionComponentData> Conditions = new List<ConditionComponentData>();

        [ListDrawerSettings(ShowFoldout = true)]
        [Searchable]
        [SerializeReference]
        public List<EffectComponentData> Effects = new List<EffectComponentData>();

        /// <summary>
        /// Get the cooldown for a specific level.
        /// </summary>
        public float GetCooldown(int level)
        {
            return Mathf.Max(0.1f, BaseCooldown - (level - 1) * CooldownReductionPerLevel);
        }

        /// <summary>
        /// Get the effect multiplier for a specific level.
        /// </summary>
        public float GetEffectMultiplier(int level)
        {
            return 1f + (level - 1) * EffectScalePerLevel;
        }
    }
#else
    [CreateAssetMenu(fileName = "NewAbility", menuName = "ZeroEngine/Ability System/Ability Data")]
    [Obsolete("Legacy ability data. Use AbilityDefinition with AbilityExecutor and IAbilityRuntimeServices for new TCE content.")]
    public class AbilityDataSO : ScriptableObject
    {
        public string AbilityName;
        [TextArea] public string Description;
        public Sprite Icon;

        [Header("Casting")]
        public float CastTime = 0f;
        public float RecoveryTime = 0f;
        public bool Interruptible = true;
        public float BaseCooldown = 1f;

        [Header("Leveling")]
        public int MaxLevel = 5;
        public float EffectScalePerLevel = 0.1f;
        public float CooldownReductionPerLevel = 0.1f;

        [SerializeReference] public List<TriggerComponentData> Triggers = new List<TriggerComponentData>();
        [SerializeReference] public List<ConditionComponentData> Conditions = new List<ConditionComponentData>();
        [SerializeReference] public List<EffectComponentData> Effects = new List<EffectComponentData>();

        public float GetCooldown(int level) => Mathf.Max(0.1f, BaseCooldown - (level - 1) * CooldownReductionPerLevel);
        public float GetEffectMultiplier(int level) => 1f + (level - 1) * EffectScalePerLevel;
    }
#endif
}
