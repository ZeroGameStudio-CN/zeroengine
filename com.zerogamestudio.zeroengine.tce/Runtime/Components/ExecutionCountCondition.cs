using System;
using System.Collections.Generic;

namespace ZeroEngine.TCE
{
    public sealed class ExecutionCountCondition : TceCondition<ExecutionCountConditionData>, ITceExecutionAcceptedObserver
    {
        private int acceptedExecutions;
        private ITceStateStore state;

        public override void OnBeforeInstall()
        {
            if (string.IsNullOrEmpty(Data.StateKey)) return;
            state = Context.State ?? throw new InvalidOperationException("An execution-count state key requires an ITceStateSource.");
        }

        public override bool Check(ITceActor target, object source)
        {
            return (state == null ? acceptedExecutions : state.ReadNumber(Data.StateKey)) < Data.MaxAcceptedExecutions;
        }

        public void OnExecutionAccepted(ITceActor target, object source)
        {
            if (state == null) acceptedExecutions++;
            else state.WriteNumber(Data.StateKey, Math.Min(Data.MaxAcceptedExecutions, state.ReadNumber(Data.StateKey) + 1));
        }
    }

    [Serializable]
    [TceComponentDoc(TceComponentDocCategory.Condition, "zeroengine.tce.condition.execution_count", "Execution Count", "Limits how many accepted executions can pass.", "Use this condition for generic one-shot or limited-use rules. The count increments only after all conditions have passed.")]
    public sealed class ExecutionCountConditionData : TceConditionData<ExecutionCountCondition>, ITceComponentDataValidator
    {
        [TceFieldDoc("Maximum number of accepted executions allowed.")]
        public int MaxAcceptedExecutions = 1;

        [TceFieldDoc("Optional stable runtime-state key. Empty retains an installation-local count; the host owns lifetime and saving.")]
        public string StateKey = string.Empty;

        public void Validate(TceComponentValidationContext context, List<TceValidationIssue> issues)
        {
            if (MaxAcceptedExecutions < 1)
                issues.Add(new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.InvalidField, $"{context.Path}.MaxAcceptedExecutions", "MaxAcceptedExecutions must be at least 1."));
        }
    }
}
