using System;
using System.Collections.Generic;
using UnityEngine;

#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace ZeroEngine.AbilitySystem
{
    // ---------------- Interface & Base ----------------

    [Obsolete("Legacy ability component data. Use AbilityTriggerDefinition, AbilityConditionDefinition and AbilityEffectDefinition for new TCE content.")]
    public abstract class ComponentData
    {
        // For serialization polymorphism (Odin handles this automatically, 
        // Unity needs [SerializeReference] if not using Odin, but we'll prioritize Odin flow as requested)
    }

    /// <summary>
    /// Base class for runtime ability components
    /// </summary>
    [Obsolete("Legacy ability component interface. Use AbilityExecutor with AbilityDefinition components for new TCE content.")]
    public interface IAbilityComponent
    {
        void Initialize(ComponentData data, object source, AbilityDataSO abilityData);
    }
    
    // ---------------- Data Definitions ----------------

    [Serializable]
    [Obsolete("Legacy trigger component data. Use AbilityTriggerDefinition for new TCE content.")]
    public abstract class TriggerComponentData : ComponentData
    {
#if ODIN_INSPECTOR
        [FoldoutGroup("Settings", Expanded = true)] 
        [LabelWidth(150)]
#endif
        public bool TriggerMultipleTimes;

#if ODIN_INSPECTOR
        [FoldoutGroup("Settings"), ShowIf(nameof(TriggerMultipleTimes))]
        [ProgressBar(1, 10, ColorGetter = "GetBarColor")]
#endif
        public int TriggerTimes = 1;

#if ODIN_INSPECTOR
        private UnityEngine.Color GetBarColor(int value) => UnityEngine.Color.Lerp(UnityEngine.Color.red, UnityEngine.Color.green, value / 10f);
#endif
    }

    [Serializable]
    [Obsolete("Legacy condition component data. Use AbilityConditionDefinition for new TCE content.")]
    public abstract class ConditionComponentData : ComponentData
    {
    }

    [Serializable]
    [Obsolete("Legacy effect component data. Use AbilityEffectDefinition for new TCE content.")]
    public abstract class EffectComponentData : ComponentData
    {
#if ODIN_INSPECTOR
        [FoldoutGroup("Settings")]
#endif
        public AbilityTargetType TargetType;
    }

    public enum AbilityTargetType
    {
        Self,
        Target,
        All
    }

    // ---------------- Concrete Examples (So the user sees something in the list) ----------------
    
    [Serializable]
#if ODIN_INSPECTOR
    [LabelText("Use Trigger (Manual)")]
    [GUIColor(0.7f, 1f, 0.7f)]
#endif
    public class ManualTriggerData : TriggerComponentData
    {
#if ODIN_INSPECTOR
        [BoxGroup("Trigger Info")]
#endif
        public string ButtonName = "Fire";
    }

    [Serializable]
#if ODIN_INSPECTOR
    [LabelText("Interval Trigger")]
    [GUIColor(0.7f, 1f, 1f)]
#endif
    public class IntervalTriggerData : TriggerComponentData
    {
        public float Interval = 1.0f;
        public bool StartImmediately = true;
    }

    [Serializable]
#if ODIN_INSPECTOR
    [LabelText("On Hit Trigger")]
    [GUIColor(1f, 0.7f, 0.2f)]
#endif
    public class OnHitTriggerData : TriggerComponentData
    {
        public bool TriggerOnDealingDamage = true;
        public bool TriggerOnTakingDamage = false;
    }

    [Serializable]
#if ODIN_INSPECTOR
    [LabelText("Deal Damage Effect")]
    [GUIColor(1f, 0.7f, 0.7f)] // Reddish
#endif
    public class DamageEffectData : EffectComponentData
    {
        public int DamageAmount = 10;
        
#if ODIN_INSPECTOR
        [HorizontalGroup("DamageInfo")]
        [HideLabel]
#endif
        public DamageType DamageType = DamageType.Physical;
    }
    
    [Serializable]
#if ODIN_INSPECTOR
    [LabelText("Heal Effect")]
#endif
    public class HealEffectData : EffectComponentData
    {
        public int HealAmount = 10;
    }

    [Serializable]
#if ODIN_INSPECTOR
    [LabelText("Spawn Projectile Effect")]
    [GUIColor(0.8f, 0.6f, 1f)]
#endif
    public class SpawnProjectileEffectData : EffectComponentData
    {
        public GameObject ProjectilePrefab;
        public float Speed = 10f;
    }

    [Serializable]
#if ODIN_INSPECTOR
    [LabelText("Apply Buff Effect")]
    [GUIColor(1f, 0.6f, 0.8f)]
#endif
    public class ApplyBuffEffectData : EffectComponentData
    {
        public ZeroEngine.BuffSystem.BuffData BuffToApply;
        public float DurationOverride = -1f; // -1 means use BuffData default
    }

    [Serializable]
#if ODIN_INSPECTOR
    [LabelText("Cooldown Condition")]
#endif
    public class CooldownConditionData : ConditionComponentData
    {
        public float CooldownSeconds = 1.0f;
    }

    [Serializable]
#if ODIN_INSPECTOR
    [LabelText("Resource Condition")]
#endif
    public class ResourceConditionData : ConditionComponentData
    {
        public ResourceType Resource = ResourceType.Mana;
        public int RequiredAmount = 10;
    }

    public enum ResourceType
    {
        Health,
        Mana,
        Stamina
    }

    public enum DamageType
    {
        Physical,
        Magical,
        True
    }
}
