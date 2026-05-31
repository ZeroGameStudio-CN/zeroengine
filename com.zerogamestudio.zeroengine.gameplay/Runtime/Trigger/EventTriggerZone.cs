using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Trigger
{
    /// <summary>
    /// Generic trigger zone that evaluates conditions and executes actions
    /// when an object with the specified tag enters the collider.
    /// </summary>
    public class EventTriggerZone : MonoBehaviour
    {
        [Header("Trigger Settings")]
        [SerializeField] private string activatorTag = "Player";
        [SerializeField] private TriggerRepeatMode repeatMode = TriggerRepeatMode.Once;
        [SerializeField] private int maxTriggerCount = 1;
        [SerializeField] private float cooldown;

        [Header("Conditions")]
        [SerializeReference]
        private List<TriggerCondition> conditions = new();

        [Header("Actions")]
        [SerializeReference]
        private List<TriggerAction> actions = new();

        private int _triggerCount;
        private float _lastTriggerTime = float.NegativeInfinity;

        public bool CanTrigger => repeatMode switch
        {
            TriggerRepeatMode.Once => _triggerCount == 0,
            TriggerRepeatMode.OncePerRun => _triggerCount == 0,
            TriggerRepeatMode.EveryEntry => true,
            TriggerRepeatMode.Count => _triggerCount < maxTriggerCount,
            _ => false
        };

        /// <summary>
        /// Reset trigger count. Call at run start for OncePerRun zones.
        /// </summary>
        public void ResetTrigger()
        {
            _triggerCount = 0;
        }

        private void OnDisable()
        {
            StopAllCoroutines();
        }

        // --- 2D ---
        private void OnTriggerEnter2D(Collider2D other)
        {
            TryTrigger(other.gameObject);
        }

        // --- 3D ---
        private void OnTriggerEnter(Collider other)
        {
            TryTrigger(other.gameObject);
        }

        private void TryTrigger(GameObject activator)
        {
            if (!CanTrigger) return;

            if (!string.IsNullOrEmpty(activatorTag) && !activator.CompareTag(activatorTag))
                return;

            if (cooldown > 0 && Time.time - _lastTriggerTime < cooldown)
                return;

            foreach (var condition in conditions)
            {
                if (condition != null && !condition.Evaluate(gameObject, activator))
                    return;
            }

            _triggerCount++;
            _lastTriggerTime = Time.time;

            foreach (var action in actions)
            {
                if (action == null) continue;
                if (action.Delay > 0)
                    StartCoroutine(DelayedExecute(action, activator));
                else
                    action.Execute(gameObject, activator);
            }
        }

        private IEnumerator DelayedExecute(TriggerAction action, GameObject activator)
        {
            yield return new WaitForSeconds(action.Delay);
            if (activator == null) yield break;
            action.Execute(gameObject, activator);
        }
    }
}
