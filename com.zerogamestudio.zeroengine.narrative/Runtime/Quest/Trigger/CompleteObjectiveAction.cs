using System;
using UnityEngine;
using ZeroEngine.Trigger;

namespace ZeroEngine.Quest.Trigger
{
    /// <summary>
    /// Trigger action that fires a condition event to advance quest objectives.
    /// </summary>
    [Serializable]
    public class CompleteObjectiveAction : TriggerAction
    {
        [Tooltip("Condition event type (e.g. Quest.Interacted, Quest.LocationReached)")]
        public string EventType = QuestEvents.Interacted;

        [Tooltip("Target ID for the condition")]
        public string TargetId;

        [Tooltip("Amount to add")]
        public int Amount = 1;

        public override void Execute(GameObject owner, GameObject activator)
        {
            if (string.IsNullOrEmpty(EventType)) return;

            var data = new ConditionEventData(TargetId, Amount)
            {
                Position = owner != null ? owner.transform.position : Vector3.zero
            };
            QuestManager.Instance?.ProcessConditionEvent(EventType, data);
        }
    }
}
