using System;
using System.Collections.Generic;

namespace ZeroEngine.TCE
{
    public interface ITceFlagSource
    {
        bool HasFlag(string flagId);
    }

    public enum TceFlagLookupTarget
    {
        Owner = 0,
        TriggerTarget = 1,
        Source = 2
    }

    public sealed class FlagCondition : TceCondition<FlagConditionData>
    {
        public override bool Check(ITceActor target, object source)
        {
            if (string.IsNullOrWhiteSpace(Data.FlagId))
                return false;

            ITceFlagSource flagSource = ResolveFlagSource(target, source);
            if (flagSource == null)
                return false;

            bool hasFlag = flagSource.HasFlag(Data.FlagId);
            return Data.Invert ? !hasFlag : hasFlag;
        }

        private ITceFlagSource ResolveFlagSource(ITceActor target, object source)
        {
            return Data.LookupTarget switch
            {
                TceFlagLookupTarget.Owner => AsFlagSource(Context.Owner),
                TceFlagLookupTarget.TriggerTarget => AsFlagSource(target),
                TceFlagLookupTarget.Source => AsFlagSource(source),
                _ => null
            };
        }

        private static ITceFlagSource AsFlagSource(object value)
        {
            if (value is ITceFlagSource flagSource)
                return flagSource;

            if (value is ITceActor actor)
                return actor.NativeObject as ITceFlagSource;

            return null;
        }
    }

    [Serializable]
    [TceComponentDoc(TceComponentDocCategory.Condition, "zeroengine.tce.condition.flag", "Flag", "Checks a generic flag source.", "Use this condition when a project adapter exposes tags, facts, states, or flags through ITceFlagSource without coupling TCE to a project-specific model.")]
    public sealed class FlagConditionData : TceConditionData<FlagCondition>, ITceComponentDataValidator
    {
        [TceFieldDoc("Flag identifier passed to the resolved flag source.")]
        public string FlagId = string.Empty;

        [TceFieldDoc("Object used to resolve the flag source.")]
        public TceFlagLookupTarget LookupTarget = TceFlagLookupTarget.Source;

        [TceFieldDoc("Invert the flag result before returning the condition result.")]
        public bool Invert;

        public void Validate(TceComponentValidationContext context, List<TceValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(FlagId))
                issues.Add(new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.InvalidField, $"{context.Path}.FlagId", "FlagId must not be empty."));

            if (!Enum.IsDefined(typeof(TceFlagLookupTarget), LookupTarget))
                issues.Add(new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.InvalidEnumValue, $"{context.Path}.LookupTarget", "LookupTarget must be a defined TceFlagLookupTarget value."));
        }
    }
}
