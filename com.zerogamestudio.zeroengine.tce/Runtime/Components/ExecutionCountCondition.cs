using System;
using System.Collections.Generic;

namespace ZeroEngine.TCE
{
    public sealed class ExecutionCountCondition : TceCondition<ExecutionCountConditionData>, ITceExecutionAcceptedObserver
    {
        private int acceptedExecutions;

        public override bool Check(ITceActor target, object source)
        {
            return acceptedExecutions < Data.MaxAcceptedExecutions;
        }

        public void OnExecutionAccepted(ITceActor target, object source)
        {
            acceptedExecutions++;
        }
    }

    [Serializable]
    [TceComponentDoc(TceComponentDocCategory.Condition, "zeroengine.tce.condition.execution_count", "Execution Count", "Limits how many accepted executions can pass.", "Use this condition for generic one-shot or limited-use rules. The count increments only after all conditions have passed.")]
    public sealed class ExecutionCountConditionData : TceConditionData<ExecutionCountCondition>, ITceComponentDataValidator
    {
        [TceFieldDoc("Maximum number of accepted executions allowed.")]
        public int MaxAcceptedExecutions = 1;

        public void Validate(TceComponentValidationContext context, List<TceValidationIssue> issues)
        {
            if (MaxAcceptedExecutions < 1)
                issues.Add(new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.InvalidField, $"{context.Path}.MaxAcceptedExecutions", "MaxAcceptedExecutions must be at least 1."));
        }
    }
}
