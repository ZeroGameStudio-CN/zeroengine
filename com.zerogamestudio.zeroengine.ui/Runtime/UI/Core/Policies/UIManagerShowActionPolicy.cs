namespace ZeroEngine.UI
{
    public readonly struct UIManagerShowActionDecision
    {
        public UIManagerShowActionDecision(bool pauseVisibleLayerSiblings, bool pauseTopView)
        {
            PauseVisibleLayerSiblings = pauseVisibleLayerSiblings;
            PauseTopView = pauseTopView;
        }

        public bool PauseVisibleLayerSiblings { get; }
        public bool PauseTopView { get; }
    }

    public static class UIManagerShowActionPolicy
    {
        public static UIManagerShowActionDecision Resolve(UIShowMode showMode)
        {
            return showMode switch
            {
                UIShowMode.HideOthers => new UIManagerShowActionDecision(true, false),
                UIShowMode.Stack => new UIManagerShowActionDecision(false, true),
                _ => default
            };
        }
    }
}
