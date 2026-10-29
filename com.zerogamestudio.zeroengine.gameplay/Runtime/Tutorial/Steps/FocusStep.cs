using System;
using System.Linq;
using UnityEngine;
using ZeroEngine.Tutorial.Guidance;
using ZeroEngine.Tutorial.Presentation;

namespace ZeroEngine.Tutorial
{
    [Serializable]
    public sealed class FocusStep : TutorialStep
    {
        public TutorialFocusRequest Focus = new TutorialFocusRequest();
        [Tooltip("Optional corner coaching from the global TutorialConfig media library.")]
        public string PresentationId;
        [NonSerialized] private TutorialFocusView view;
        [NonSerialized] private ITutorialPresentation presentation;
        [NonSerialized] private Func<System.Collections.Generic.IReadOnlyList<RectTransform>> priorExclusions;
        [NonSerialized] private Func<System.Collections.Generic.IReadOnlyList<RectTransform>> ownedExclusions;
        public override string StepType => "Focus";
        public override bool OwnsBlockingPresentation => Focus != null && Focus.BlockOtherInput;
        public override bool SupportsAutomaticCompletion => Focus != null && Focus.Interaction == TutorialInteraction.Display;

        public override void OnEnter(TutorialContext ctx)
        {
            view = ctx.FocusView ? ctx.FocusView : TutorialUIManager.Instance.GetFocusView();
            if (!string.IsNullOrEmpty(PresentationId))
            {
                var media = ctx.Config ? ctx.Config.FindMedia(PresentationId) : null;
                if (media == null || media.DialogueLayout != TutorialDialogueLayout.Corner ||
                    (media.Kind != TutorialMediaKind.Text && media.Kind != TutorialMediaKind.Dialogue))
                    throw new InvalidOperationException("Focus coaching must reference corner dialogue/text media.");
                if (ctx.Presentations.DefaultPlayer == null) ctx.Presentations.DefaultPlayer = TutorialUIManager.Instance.GetPresentationPlayer();
                presentation = ctx.Presentations.Begin(media, ctx, CanSkip);
                priorExclusions = ctx.Focus.VisibleExclusions;
                ownedExclusions = () => presentation.VisibleContent;
                ctx.Focus.VisibleExclusions = ownedExclusions;
            }
            view.Begin(Focus.RuntimeCopy(CanSkip && (!ctx.Config || ctx.Config.AllowSkipping)), ctx.Focus, ctx.Config ? ctx.Config.FocusStyle : new TutorialFocusStyle());
        }

        public override void OnUpdate(TutorialContext ctx)
        {
            presentation?.Tick(true);
            if (presentation != null && presentation.Result == TutorialPresentationResult.Failed) ctx.Fail(presentation.Error);
            if (presentation != null && presentation.Result == TutorialPresentationResult.Cancelled) ctx.Cancel();
            view?.Tick();
            if (view && view.Result == TutorialFocusResult.Failed) ctx.Fail(view.Error);
            else if (view && view.Result == TutorialFocusResult.Cancelled) ctx.Cancel();
            else if ((view && view.Result == TutorialFocusResult.Skipped) || presentation?.Result == TutorialPresentationResult.Skipped)
            { if (CanSkip) ctx.RequestStepSkip(); else ctx.Fail("A required operation was skipped by its presentation backend."); }
        }
        public override bool IsCompleted(TutorialContext ctx) => view && view.Result == TutorialFocusResult.Completed;
        public override void OnExit(TutorialContext ctx)
        {
            if (view) view.Cancel(); view = null;
            if (presentation != null)
            {
                presentation.Dispose(); presentation = null;
                if (ReferenceEquals(ctx.Focus.VisibleExclusions, ownedExclusions)) ctx.Focus.VisibleExclusions = priorExclusions;
            }
        }
        public override bool Validate(out string error)
        {
            var errors = Focus?.Validate(); error = Focus == null ? "A focus request is required." : errors.Count > 0 ? string.Join("\n", errors) : null;
            return error == null;
        }
    }

    [Serializable]
    public sealed class PresentationStep : TutorialStep
    {
        public string MediaId;
        [NonSerialized] private ITutorialPresentation presentation;
        public override string StepType => "Presentation";
        public override bool OwnsBlockingPresentation => true;
        public override void OnEnter(TutorialContext ctx)
        {
            var media = ctx.Config ? ctx.Config.FindMedia(MediaId) : null;
            if (media == null) throw new InvalidOperationException("Presentation media is missing: " + MediaId);
            if (ctx.Presentations.DefaultPlayer == null) ctx.Presentations.DefaultPlayer = TutorialUIManager.Instance.GetPresentationPlayer();
            presentation = ctx.Presentations.Begin(media, ctx, CanSkip && (!ctx.Config || ctx.Config.AllowSkipping));
        }
        public override void OnUpdate(TutorialContext ctx)
        {
            presentation.Tick();
            if (presentation.Result == TutorialPresentationResult.Failed) ctx.Fail(presentation.Error);
            else if (presentation.Result == TutorialPresentationResult.Cancelled) ctx.Cancel();
            else if (presentation.Result == TutorialPresentationResult.Skipped)
            { if (CanSkip) ctx.RequestStepSkip(); else ctx.Fail("A required presentation was skipped by its backend."); }
        }
        public override bool IsCompleted(TutorialContext ctx) => presentation?.Result == TutorialPresentationResult.Completed;
        public override void OnExit(TutorialContext ctx) { presentation?.Dispose(); presentation = null; }
        public override bool Validate(out string error) { error = string.IsNullOrWhiteSpace(MediaId) ? "A media ID is required." : null; return error == null; }
    }
}
