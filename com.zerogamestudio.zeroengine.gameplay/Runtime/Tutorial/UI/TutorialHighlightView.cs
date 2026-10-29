using System;
using UnityEngine;
using ZeroEngine.Tutorial.Guidance;

namespace ZeroEngine.Tutorial
{
    public class TutorialHighlightView : MonoBehaviour
    {
        private TutorialUIManager manager;
        private TutorialFocusView focus;
        private TutorialContext context;
        private IDisposable target;
        private RectTransform current;
        private HighlightType type;
        private string hint;
        private bool waitForClick = true;

        public void Initialize(TutorialUIManager owner)
        {
            manager = owner;
            context = owner.PresentationContext;
            focus = owner.GetFocusView();
        }
        public void ShowHighlight(RectTransform element, HighlightType highlightType, string hintText = null)
        {
            Hide(); current = element; type = highlightType; hint = hintText;
            if (!current || !manager) return;
            if (!string.IsNullOrWhiteSpace(hint)) manager.ShowPrompt(hint, null);
            target = context.Focus.Targets.Register("prefab-highlight", current.gameObject);
            var request = new TutorialFocusRequest { Interaction = waitForClick ? TutorialInteraction.Click : TutorialInteraction.Display,
                DisplaySeconds = float.MaxValue, BlockOtherInput = waitForClick,
                CanSkip = !manager.Config || manager.Config.AllowSkipping };
            request.Targets.Add(new TutorialTargetReference { Id = "prefab-highlight", Kind = TutorialTargetKind.UI,
                Shape = type == HighlightType.Circle ? TutorialMaskShape.Circle : TutorialMaskShape.Rectangle });
            focus.Begin(request, context.Focus, manager.Config ? manager.Config.FocusStyle : new TutorialFocusStyle(),
                result => { if (result == TutorialFocusResult.Completed) manager.TriggerHighlightTargetClicked(); });
        }
        public void Hide() { if (focus) focus.Cancel(); target?.Dispose(); target = null; }
        public void SetWaitForClick(bool wait)
        {
            if (waitForClick == wait) return; waitForClick = wait;
            if (current && focus && focus.IsActive) ShowHighlight(current, type, hint);
        }
        private void OnDisable() => Hide();
        private void OnDestroy() => Hide();
    }
}
