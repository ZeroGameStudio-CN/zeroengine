using System;
using UnityEngine;
using ZeroEngine.Tutorial.Guidance;
using ZeroEngine.Tutorial.Presentation;

namespace ZeroEngine.Tutorial
{
    /// <summary>
    /// 教程步骤基类 (v1.14.0+)
    /// 使用 [SerializeReference] 实现多态序列化
    /// </summary>
    [Serializable]
    public abstract class TutorialStep
    {
        #region Serialized Fields

        [Tooltip("步骤 ID (可选，用于跳转)")]
        public string StepId;

        [Tooltip("步骤描述 (仅编辑器显示)")]
        public string Description;

        [Tooltip("是否可跳过")]
        public bool CanSkip = true;

        [Tooltip("自动完成延迟 (秒，0 表示不自动完成)")]
        public float AutoCompleteDelay = 0f;

        [HideInInspector]
        [Tooltip("编辑器位置 (GraphView 用)")]
        public Vector2 EditorPosition;

        #endregion

        #region Abstract Methods

        /// <summary>
        /// 步骤类型名称 (用于编辑器显示)
        /// </summary>
        public abstract string StepType { get; }
        public virtual bool OwnsBlockingPresentation => false;
        public virtual bool SupportsAutomaticCompletion => true;

        /// <summary>
        /// 步骤进入时调用
        /// </summary>
        public abstract void OnEnter(TutorialContext ctx);

        /// <summary>
        /// 每帧更新时调用
        /// </summary>
        public abstract void OnUpdate(TutorialContext ctx);

        /// <summary>
        /// 步骤退出时调用
        /// </summary>
        public abstract void OnExit(TutorialContext ctx);

        /// <summary>
        /// 检查步骤是否完成
        /// </summary>
        public abstract bool IsCompleted(TutorialContext ctx);

        #endregion

        #region Virtual Methods

        /// <summary>
        /// 获取步骤简短描述 (用于 UI 显示)
        /// </summary>
        public virtual string GetDisplayText()
        {
            return Description ?? StepType;
        }

        /// <summary>
        /// 当步骤被跳过时调用
        /// </summary>
        public virtual void OnSkip(TutorialContext ctx)
        {
            // The runner owns OnExit for every terminal path, including skips.
        }

        /// <summary>
        /// 验证步骤配置 (编辑器用)
        /// </summary>
        public virtual bool Validate(out string error)
        {
            error = null;
            return true;
        }

        #endregion
    }

    [Serializable]
    public class DialogueStep : TutorialStep
    {
        public string SpeakerName;
        [TextArea(2, 5)] public string DialogueText;
        public Sprite SpeakerIcon;
        public DialoguePosition Position = DialoguePosition.Bottom;
        public float TypewriterSpeed = 30f;
        public bool WaitForConfirm = true;
        public KeyCode ConfirmKey = KeyCode.Space;
        [NonSerialized] private ITutorialPresentation presentation;
        public override string StepType => "Dialogue";
        public override bool OwnsBlockingPresentation => true;
        public override void OnEnter(TutorialContext ctx)
        {
            if (ctx.Presentations.DefaultPlayer == null) ctx.Presentations.DefaultPlayer = TutorialUIManager.Instance.GetPresentationPlayer();
            presentation = ctx.Presentations.Begin(new TutorialMedia {
                Id = string.IsNullOrEmpty(StepId) ? "inline-dialogue" : StepId, Kind = TutorialMediaKind.Dialogue,
                Speaker = SpeakerName, Text = DialogueText, Portrait = SpeakerIcon, TypewriterSpeed = TypewriterSpeed,
                DialogueLayout = Position == DialoguePosition.Top ? TutorialDialogueLayout.Corner : TutorialDialogueLayout.Normal,
                Advance = WaitForConfirm ? TutorialAdvanceMode.Manual : TutorialAdvanceMode.Timed,
                Seconds = AutoCompleteDelay > 0 ? AutoCompleteDelay : 3
            }, ctx, CanSkip && (!ctx.Config || ctx.Config.AllowSkipping));
        }
        public override void OnUpdate(TutorialContext ctx)
        {
            presentation.Tick();
            if (WaitForConfirm && TutorialInputHelper.GetKeyDown(ConfirmKey)) presentation.Continue();
            if (presentation.Result == TutorialPresentationResult.Failed) ctx.Fail(presentation.Error);
            else if (presentation.Result == TutorialPresentationResult.Cancelled) ctx.Cancel();
            else if (presentation.Result == TutorialPresentationResult.Skipped) ctx.RequestStepSkip();
        }
        public override void OnExit(TutorialContext ctx) { presentation?.Dispose(); presentation = null; }
        public override bool IsCompleted(TutorialContext ctx) => presentation?.Result == TutorialPresentationResult.Completed;
        public override string GetDisplayText() => string.IsNullOrEmpty(SpeakerName) ? DialogueText : SpeakerName + ": " + DialogueText;
        public override bool Validate(out string error) { error = string.IsNullOrWhiteSpace(DialogueText) ? "Dialogue text is required." : null; return error == null; }
    }

    public enum DialoguePosition { Top = 0, Bottom = 1, Center = 2, Custom = 3 }

    [Serializable]
    public class HighlightStep : TutorialStep
    {
        public string TargetPath;
        public HighlightType HighlightType = HighlightType.Circle;
        [TextArea(1, 3)] public string HintText;
        public Vector2 HintOffset = Vector2.zero;
        public bool WaitForClick = true;
        public float Timeout;
        [NonSerialized] private TutorialFocusView view;
        [NonSerialized] private ITutorialPresentation presentation;
        [NonSerialized] private IDisposable registration;
        [NonSerialized] private Func<System.Collections.Generic.IReadOnlyList<RectTransform>> oldExclusions, exclusions;
        public override string StepType => "Highlight";
        public override bool OwnsBlockingPresentation => WaitForClick;
        public override bool SupportsAutomaticCompletion => !WaitForClick;
        public override void OnEnter(TutorialContext ctx)
        {
            view = ctx.FocusView ? ctx.FocusView : TutorialUIManager.Instance.GetFocusView();
            string id = "highlight:" + TargetPath;
            var found = ctx.FindUITarget(TargetPath);
            if (found && !ctx.Focus.Targets.Find(id)) registration = ctx.Focus.Targets.Register(id, found.gameObject);
            if (!string.IsNullOrEmpty(HintText))
            {
                if (ctx.Presentations.DefaultPlayer == null) ctx.Presentations.DefaultPlayer = TutorialUIManager.Instance.GetPresentationPlayer();
                presentation = ctx.Presentations.Begin(new TutorialMedia { Id = "highlight-hint", Kind = TutorialMediaKind.Text,
                    DialogueLayout = TutorialDialogueLayout.Corner, Text = HintText }, ctx, CanSkip && (!ctx.Config || ctx.Config.AllowSkipping));
                oldExclusions = ctx.Focus.VisibleExclusions; exclusions = () => presentation.VisibleContent; ctx.Focus.VisibleExclusions = exclusions;
                foreach (var rect in presentation.VisibleContent) rect.anchoredPosition += HintOffset;
            }
            var options = new TutorialFocusRequest { Interaction = WaitForClick ? TutorialInteraction.Click : TutorialInteraction.Display,
                BlockOtherInput = WaitForClick, CanSkip = CanSkip && (!ctx.Config || ctx.Config.AllowSkipping),
                CompletionTimeoutSeconds = WaitForClick ? Timeout : 0, DisplaySeconds = Timeout > 0 ? Timeout : 3 };
            options.Targets.Add(new TutorialTargetReference { Id = id, Path = TargetPath, Kind = TutorialTargetKind.UI,
                Shape = HighlightType == HighlightType.Circle ? TutorialMaskShape.Circle : TutorialMaskShape.Rectangle });
            view.Begin(options, ctx.Focus, ctx.Config ? ctx.Config.FocusStyle : new TutorialFocusStyle());
        }
        public override void OnUpdate(TutorialContext ctx)
        {
            presentation?.Tick(true); view?.Tick();
            if (view && view.Result == TutorialFocusResult.Failed) ctx.Fail(view.Error);
            else if (view && view.Result == TutorialFocusResult.Cancelled) ctx.Cancel();
            else if (view && view.Result == TutorialFocusResult.Skipped) ctx.RequestStepSkip();
            if (presentation?.Result == TutorialPresentationResult.Failed) ctx.Fail(presentation.Error);
        }
        public override void OnExit(TutorialContext ctx)
        {
            if (view) view.Cancel(); view = null;
            registration?.Dispose(); registration = null;
            presentation?.Dispose(); presentation = null;
            if (ReferenceEquals(ctx.Focus.VisibleExclusions, exclusions)) ctx.Focus.VisibleExclusions = oldExclusions;
        }
        public override bool IsCompleted(TutorialContext ctx) => view && view.Result == TutorialFocusResult.Completed;
        public override bool Validate(out string error) { error = string.IsNullOrWhiteSpace(TargetPath) ? "A target path is required." : null; return error == null; }
    }
}
