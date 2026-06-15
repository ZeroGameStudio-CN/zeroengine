using System;

namespace ZeroEngine.TCE
{
    public abstract class TceComponentData
    {
        public abstract Type RuntimeType { get; }
    }

    [Serializable]
    public abstract class TceTriggerData : TceComponentData
    {
        [TceFieldDoc("Trigger ordering value used by authoring tools and docs.")]
        public int Order;
    }

    [Serializable]
    public abstract class TceTriggerData<TTrigger> : TceTriggerData where TTrigger : ITceTrigger
    {
        public override Type RuntimeType => typeof(TTrigger);
    }

    [Serializable]
    public abstract class TceConditionData : TceComponentData
    {
    }

    [Serializable]
    public abstract class TceConditionData<TCondition> : TceConditionData where TCondition : ITceCondition
    {
        public override Type RuntimeType => typeof(TCondition);
    }

    [Serializable]
    public abstract class TceEffectData : TceComponentData
    {
        [TceFieldDoc("Target actor selection used by the effect.")]
        public TceTargetMode Target = TceTargetMode.FromTrigger;
    }

    [Serializable]
    public abstract class TceEffectData<TEffect> : TceEffectData where TEffect : ITceEffect
    {
        public override Type RuntimeType => typeof(TEffect);
    }

    public enum TceTargetMode
    {
        FromTrigger = 0,
        Self = 1,
        Source = 2
    }
}
