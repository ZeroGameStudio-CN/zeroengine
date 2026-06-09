using System;
using System.Collections.Generic;

namespace ZeroEngine.TCE
{
    public interface ITceRandomSource
    {
        float Next01();
    }

    public enum TceRandomLookupTarget
    {
        Owner = 0,
        TriggerTarget = 1,
        InstallSource = 2,
        TriggerSource = 3
    }

    public sealed class ChanceCondition : TceCondition<ChanceConditionData>
    {
        public override bool Check(ITceActor target, object source)
        {
            ITceRandomSource randomSource = ResolveRandomSource(target, source);
            if (randomSource == null)
                return false;

            if (Data.Chance <= 0f)
                return false;

            if (Data.Chance >= 1f)
                return true;

            return randomSource.Next01() < Data.Chance;
        }

        private ITceRandomSource ResolveRandomSource(ITceActor target, object source)
        {
            return Data.LookupTarget switch
            {
                TceRandomLookupTarget.Owner => AsRandomSource(Context.Owner),
                TceRandomLookupTarget.TriggerTarget => AsRandomSource(target),
                TceRandomLookupTarget.InstallSource => AsRandomSource(Context.InstallSource),
                TceRandomLookupTarget.TriggerSource => AsRandomSource(source),
                _ => null
            };
        }

        private static ITceRandomSource AsRandomSource(object value)
        {
            if (value is ITceRandomSource randomSource)
                return randomSource;

            if (value is ITceActor actor)
                return actor.NativeObject as ITceRandomSource;

            return null;
        }
    }

    [Serializable]
    [TceComponentDoc(TceComponentDocCategory.Condition, "Chance", "Passes based on a deterministic random source.", "Use this condition when a trigger, owner, or install source exposes ITceRandomSource. The generic package does not own project RNG state.")]
    public sealed class ChanceConditionData : TceConditionData<ChanceCondition>, ITceComponentDataValidator
    {
        public float Chance = 1f;
        public TceRandomLookupTarget LookupTarget = TceRandomLookupTarget.TriggerSource;

        public void Validate(TceComponentValidationContext context, List<TceValidationIssue> issues)
        {
            if (Chance < 0f || Chance > 1f)
                issues.Add(new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.InvalidField, $"{context.Path}.Chance", "Chance must be between 0 and 1."));

            if (!Enum.IsDefined(typeof(TceRandomLookupTarget), LookupTarget))
                issues.Add(new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.InvalidEnumValue, $"{context.Path}.LookupTarget", "LookupTarget must be a defined TceRandomLookupTarget value."));
        }
    }
}
