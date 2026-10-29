using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ZeroEngine.Tutorial
{
    [Serializable]
    public class CompositeStep : TutorialStep
    {
        public enum ExecutionMode { Sequential = 0, Parallel = 1, Any = 2 }
        public ExecutionMode Mode = ExecutionMode.Sequential;
        [SerializeReference] public List<TutorialStep> SubSteps = new List<TutorialStep>();
        [NonSerialized] private bool[] entered, completed;
        [NonSerialized] private int index;
        [NonSerialized] private bool done;
        public override string StepType => "Composite (" + Mode + ")";
        public override bool OwnsBlockingPresentation => SubSteps != null && SubSteps.Any(step => step != null && step.OwnsBlockingPresentation);
        public override bool SupportsAutomaticCompletion => SubSteps != null && SubSteps.All(step => step != null && step.SupportsAutomaticCompletion);

        public override void OnEnter(TutorialContext ctx)
        {
            entered = new bool[SubSteps.Count]; completed = new bool[SubSteps.Count]; index = 0; done = SubSteps.Count == 0;
            if (Mode == ExecutionMode.Sequential) { if (!done) Enter(0, ctx); }
            else for (int i = 0; i < SubSteps.Count; i++) Enter(i, ctx);
        }
        private void Enter(int item, TutorialContext ctx)
        {
            if (SubSteps[item] == null) { completed[item] = true; return; }
            entered[item] = true;
            SubSteps[item].OnEnter(ctx);
        }
        private void Exit(int item, TutorialContext ctx)
        {
            if (entered == null || !entered[item]) return;
            entered[item] = false;
            SubSteps[item]?.OnExit(ctx);
        }
        public override void OnUpdate(TutorialContext ctx)
        {
            if (done) return;
            int first = Mode == ExecutionMode.Sequential ? index : 0;
            int end = Mode == ExecutionMode.Sequential ? Math.Min(index + 1, SubSteps.Count) : SubSteps.Count;
            for (int i = first; i < end; i++)
            {
                if (completed[i]) continue;
                var step = SubSteps[i]; step?.OnUpdate(ctx);
                if (!string.IsNullOrEmpty(ctx.Failure) || ctx.CancelRequested || ctx.StepSkipRequested) return;
                if (step != null && !step.IsCompleted(ctx)) continue;
                Exit(i, ctx); completed[i] = true;
                if (Mode == ExecutionMode.Any) { done = true; OnExit(ctx); return; }
            }
            if (Mode == ExecutionMode.Sequential && completed[index])
            {
                index++;
                if (index < SubSteps.Count) Enter(index, ctx); else done = true;
            }
            else if (Mode != ExecutionMode.Sequential) done = completed.All(value => value);
        }
        public override void OnExit(TutorialContext ctx)
        {
            if (entered == null) return;
            Exception failure = null;
            for (int i = 0; i < entered.Length; i++)
                try { Exit(i, ctx); } catch (Exception exception) { failure = failure ?? exception; }
            if (failure != null) throw failure;
        }
        public override bool IsCompleted(TutorialContext ctx) => done;
        public override void OnSkip(TutorialContext ctx)
        {
            if (entered == null) return;
            for (int i = 0; i < entered.Length; i++) if (entered[i]) SubSteps[i]?.OnSkip(ctx);
        }
        public override string GetDisplayText() => string.IsNullOrEmpty(Description) ? Mode + " (" + SubSteps.Count + " steps)" : Description;
        public override bool Validate(out string error)
        {
            error = null;
            if (SubSteps == null || SubSteps.Count == 0) error = "At least one sub-step is required.";
            else if (Mode != ExecutionMode.Sequential && SubSteps.Count(step => step != null && step.OwnsBlockingPresentation) > 1)
                error = "Parallel steps may have only one blocking presenter. Put corner coaching on the Focus step.";
            else for (int i = 0; i < SubSteps.Count; i++)
            {
                if (SubSteps[i] == null) { error = "A sub-step is null."; break; }
                if (!SubSteps[i].Validate(out var problem)) { error = "Sub-step " + i + ": " + problem; break; }
            }
            return error == null;
        }
    }
}
