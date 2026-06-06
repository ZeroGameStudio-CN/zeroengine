using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.TCE
{
    [Serializable]
    public sealed class TceGraph
    {
        [SerializeReference] private List<TceTriggerData> triggers = new();
        [SerializeReference] private List<TceConditionData> conditions = new();
        [SerializeReference] private List<TceEffectData> effects = new();

        public IReadOnlyList<TceTriggerData> Triggers => triggers;
        public IReadOnlyList<TceConditionData> Conditions => conditions;
        public IReadOnlyList<TceEffectData> Effects => effects;

        public void AddTrigger(TceTriggerData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            triggers.Add(data);
        }

        public void AddCondition(TceConditionData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            conditions.Add(data);
        }

        public void AddEffect(TceEffectData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            effects.Add(data);
        }
    }
}
