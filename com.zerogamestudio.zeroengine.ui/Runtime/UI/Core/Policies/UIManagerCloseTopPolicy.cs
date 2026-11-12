namespace ZeroEngine.UI
{
    public readonly struct UIManagerCloseTopDecision
    {
        public UIManagerCloseTopDecision(bool closeTopView)
        {
            CloseTopView = closeTopView;
        }

        public bool CloseTopView { get; }
    }

    public static class UIManagerCloseTopPolicy
    {
        public static UIManagerCloseTopDecision Resolve(bool hasTopView, bool allowEscClose)
        {
            return new UIManagerCloseTopDecision(hasTopView && allowEscClose);
        }
    }
}
