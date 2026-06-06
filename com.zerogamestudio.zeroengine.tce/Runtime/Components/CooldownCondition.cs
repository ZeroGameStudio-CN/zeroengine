using System;
using UnityEngine;

namespace ZeroEngine.TCE
{
    public sealed class CooldownCondition : TceCondition<CooldownConditionData>, ITceExecutionAcceptedObserver
    {
        private float nextAllowedTime = float.NegativeInfinity;

        public override bool Check(ITceActor target, object source)
        {
            return Context.Clock.Now >= nextAllowedTime;
        }

        public void OnExecutionAccepted(ITceActor target, object source)
        {
            nextAllowedTime = Context.Clock.Now + Mathf.Max(0f, Data.Duration);
        }
    }

    [Serializable]
    [TceComponentDoc(TceComponentDocCategory.Condition, "Cooldown", "Prevents repeated accepted executions for a duration.", "Cooldown starts only after every condition has passed, then before effects run, so failed later conditions do not consume cooldown and synchronous reentry is blocked.")]
    public sealed class CooldownConditionData : TceConditionData<CooldownCondition>
    {
        public float Duration = 1f;
    }
}
