using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.AbilitySystem
{
    public interface IAbilitySource
    {
        Transform Transform { get; }
    }

    public interface IAbilityTarget
    {
        Transform Transform { get; }
    }

    /// <summary>
    /// Ability casting state.
    /// </summary>
    public enum AbilityCastState
    {
        Idle,
        Casting,      // In cast time (interruptible)
        Executing,    // Effects are being applied
        Recovering    // Post-cast recovery
    }

    /// <summary>
    /// Runtime instance of an ability with level and cooldown tracking.
    /// </summary>
    [Serializable]
    [Obsolete("Legacy runtime ability instance. Use AbilityDefinition with AbilityExecutor and project IAbilityRuntimeServices adapters for new TCE content.")]
    public class AbilityInstance
    {
        public AbilityDataSO Data;
        public int Level = 1;
        public float CooldownRemaining;

        public bool IsOnCooldown => CooldownRemaining > 0;
        public float Cooldown => Data.GetCooldown(Level);
        public float EffectMultiplier => Data.GetEffectMultiplier(Level);

        public AbilityInstance(AbilityDataSO data, int level = 1)
        {
            Data = data;
            Level = Mathf.Clamp(level, 1, data.MaxLevel);
            CooldownRemaining = 0;
        }

        public bool TryLevelUp()
        {
            if (Level < Data.MaxLevel)
            {
                Level++;
                return true;
            }
            return false;
        }

        public void StartCooldown()
        {
            CooldownRemaining = Cooldown;
        }

        public void UpdateCooldown(float deltaTime)
        {
            if (CooldownRemaining > 0)
            {
                CooldownRemaining = Mathf.Max(0, CooldownRemaining - deltaTime);
            }
        }

        public void ResetCooldown()
        {
            CooldownRemaining = 0;
        }
    }

    /// <summary>
    /// Event args for ability events.
    /// </summary>
    public struct AbilityEventArgs
    {
        public AbilityInstance Ability;
        public IAbilityTarget Target;
        public AbilityCastState PreviousState;
        public AbilityCastState NewState;
        public string InterruptReason;
    }

    /// <summary>
    /// Handles ability casting, cooldowns, interruption, and effects.
    /// </summary>
    [Obsolete("Legacy ability handler. Use AbilityExecutor and project flow adapters for new TCE content.")]
    public class AbilityHandler : MonoBehaviour, IAbilitySource
    {
        [SerializeField] private List<AbilityInstance> _abilities = new List<AbilityInstance>();

        public AbilityCastState CurrentState { get; private set; } = AbilityCastState.Idle;
        public AbilityInstance CurrentCastingAbility { get; private set; }
        public float CastProgress { get; private set; }

        public Transform Transform => transform;
        public IReadOnlyList<AbilityInstance> Abilities => _abilities;

        /// <summary>
        /// Event fired when ability state changes.
        /// </summary>
        public event Action<AbilityEventArgs> OnAbilityStateChanged;

        /// <summary>
        /// Event fired when an ability is interrupted.
        /// </summary>
        public event Action<AbilityEventArgs> OnAbilityInterrupted;

        /// <summary>
        /// Event fired when ability effects are executed.
        /// </summary>
        public event Action<AbilityInstance, IAbilityTarget, float> OnAbilityExecuted;

        private Coroutine _castCoroutine;
        private IAbilityTarget _currentTarget;

        private void Update()
        {
            // Update cooldowns - use for loop to avoid GC allocation from enumerator
            float dt = Time.deltaTime;
            int count = _abilities.Count;
            for (int i = 0; i < count; i++)
            {
                _abilities[i].UpdateCooldown(dt);
            }
        }

        /// <summary>
        /// Add an ability to this handler.
        /// </summary>
        public AbilityInstance AddAbility(AbilityDataSO data, int level = 1)
        {
            var instance = new AbilityInstance(data, level);
            _abilities.Add(instance);
            return instance;
        }

        /// <summary>
        /// Remove an ability from this handler.
        /// </summary>
        public bool RemoveAbility(AbilityDataSO data)
        {
            // Manual loop to avoid Lambda closure GC allocation
            int count = _abilities.Count;
            for (int i = count - 1; i >= 0; i--)
            {
                if (_abilities[i].Data == data)
                {
                    _abilities.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Get an ability instance by its data.
        /// </summary>
        public AbilityInstance GetAbility(AbilityDataSO data)
        {
            // Manual loop to avoid Lambda closure GC allocation
            int count = _abilities.Count;
            for (int i = 0; i < count; i++)
            {
                if (_abilities[i].Data == data)
                    return _abilities[i];
            }
            return null;
        }

        /// <summary>
        /// Check if an ability can be cast.
        /// </summary>
        public bool CanCast(AbilityInstance ability)
        {
            if (ability == null || ability.Data == null) return false;
            if (ability.IsOnCooldown) return false;
            if (CurrentState != AbilityCastState.Idle) return false;

            // Check conditions
            foreach (var condition in ability.Data.Conditions)
            {
                if (!CheckCondition(condition, ability))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Start casting an ability.
        /// </summary>
        public bool TryCastAbility(AbilityDataSO abilityData, IAbilityTarget target = null)
        {
            var instance = GetAbility(abilityData);
            if (instance == null)
            {
                Debug.LogWarning($"[AbilityHandler] Ability {abilityData.AbilityName} not found on handler.");
                return false;
            }

            return TryCastAbility(instance, target);
        }

        /// <summary>
        /// Start casting an ability instance.
        /// </summary>
        public bool TryCastAbility(AbilityInstance ability, IAbilityTarget target = null)
        {
            if (!CanCast(ability))
            {
                Debug.Log($"[AbilityHandler] Cannot cast {ability.Data.AbilityName}: " +
                         (ability.IsOnCooldown ? $"on cooldown ({ability.CooldownRemaining:F1}s)" : "conditions not met"));
                return false;
            }

            _currentTarget = target;
            CurrentCastingAbility = ability;

            if (_castCoroutine != null)
            {
                StopCoroutine(_castCoroutine);
            }

            _castCoroutine = StartCoroutine(CastCoroutine(ability, target));
            return true;
        }

        /// <summary>
        /// Interrupt the current cast.
        /// </summary>
        public bool TryInterrupt(string reason = "Interrupted")
        {
            if (CurrentState != AbilityCastState.Casting)
                return false;

            if (CurrentCastingAbility == null || !CurrentCastingAbility.Data.Interruptible)
                return false;

            if (_castCoroutine != null)
            {
                StopCoroutine(_castCoroutine);
                _castCoroutine = null;
            }

            var args = new AbilityEventArgs
            {
                Ability = CurrentCastingAbility,
                Target = _currentTarget,
                PreviousState = CurrentState,
                NewState = AbilityCastState.Idle,
                InterruptReason = reason
            };

            Debug.Log($"[AbilityHandler] {CurrentCastingAbility.Data.AbilityName} interrupted: {reason}");

            CurrentState = AbilityCastState.Idle;
            CastProgress = 0;
            CurrentCastingAbility = null;

            OnAbilityInterrupted?.Invoke(args);
            OnAbilityStateChanged?.Invoke(args);

            return true;
        }

        private IEnumerator CastCoroutine(AbilityInstance ability, IAbilityTarget target)
        {
            var data = ability.Data;

            // Cast phase
            if (data.CastTime > 0)
            {
                SetState(AbilityCastState.Casting, ability, target);
                CastProgress = 0;

                float elapsed = 0;
                while (elapsed < data.CastTime)
                {
                    elapsed += Time.deltaTime;
                    CastProgress = elapsed / data.CastTime;
                    yield return null;
                }
            }

            // Execute phase
            SetState(AbilityCastState.Executing, ability, target);
            ExecuteAbility(ability, target);
            ability.StartCooldown();

            // Recovery phase
            if (data.RecoveryTime > 0)
            {
                SetState(AbilityCastState.Recovering, ability, target);
                yield return new WaitForSeconds(data.RecoveryTime);
            }

            // Done
            SetState(AbilityCastState.Idle, null, null);
            CastProgress = 0;
            CurrentCastingAbility = null;
            _castCoroutine = null;
        }

        private void SetState(AbilityCastState newState, AbilityInstance ability, IAbilityTarget target)
        {
            var prevState = CurrentState;
            CurrentState = newState;

            OnAbilityStateChanged?.Invoke(new AbilityEventArgs
            {
                Ability = ability,
                Target = target,
                PreviousState = prevState,
                NewState = newState
            });
        }

        private void ExecuteAbility(AbilityInstance ability, IAbilityTarget target)
        {
            Debug.Log($"[AbilityHandler] Executing {ability.Data.AbilityName} (Lv.{ability.Level}, x{ability.EffectMultiplier:F2})");

            foreach (var effectData in ability.Data.Effects)
            {
                ResolveEffect(effectData, target, ability.EffectMultiplier);
            }

            OnAbilityExecuted?.Invoke(ability, target, ability.EffectMultiplier);
        }

        private void ResolveEffect(EffectComponentData data, IAbilityTarget target, float multiplier)
        {
            if (data is DamageEffectData damageEffect)
            {
                int scaledDamage = Mathf.RoundToInt(damageEffect.DamageAmount * multiplier);
                Debug.Log($"  → Dealt {scaledDamage} ({damageEffect.DamageType}) damage");
            }
            else if (data is HealEffectData healEffect)
            {
                int scaledHeal = Mathf.RoundToInt(healEffect.HealAmount * multiplier);
                Debug.Log($"  → Healed {scaledHeal}");
            }
            else if (data is ApplyBuffEffectData buffEffect)
            {
                Debug.Log($"  → Applied buff: {buffEffect.BuffToApply?.name ?? "null"}");
            }
            else if (data is SpawnProjectileEffectData projectileEffect)
            {
                Debug.Log($"  → Spawned projectile at speed {projectileEffect.Speed}");
            }
            else
            {
                Debug.Log($"  → Processed effect: {data.GetType().Name}");
            }
        }

        private bool CheckCondition(ConditionComponentData condition, AbilityInstance ability)
        {
            // Conditions are checked but actual resource deduction would happen elsewhere
            if (condition is CooldownConditionData)
            {
                // Cooldown is already checked via ability.IsOnCooldown
                return true;
            }
            else if (condition is ResourceConditionData resourceCondition)
            {
                // TODO: Integrate with a resource system
                // For now, always pass
                Debug.Log($"  [Condition] Requires {resourceCondition.RequiredAmount} {resourceCondition.Resource}");
                return true;
            }

            return true;
        }

        /// <summary>
        /// Level up an ability.
        /// </summary>
        public bool TryLevelUpAbility(AbilityDataSO data)
        {
            var instance = GetAbility(data);
            if (instance == null) return false;
            return instance.TryLevelUp();
        }

        /// <summary>
        /// Reset cooldown for a specific ability.
        /// </summary>
        public void ResetCooldown(AbilityDataSO data)
        {
            GetAbility(data)?.ResetCooldown();
        }

        /// <summary>
        /// Reset all cooldowns.
        /// </summary>
        public void ResetAllCooldowns()
        {
            foreach (var ability in _abilities)
            {
                ability.ResetCooldown();
            }
        }
    }
}
