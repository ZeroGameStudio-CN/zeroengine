namespace ZeroEngine.UI
{
    public readonly struct UIManagerViewToggleDecision
    {
        public UIManagerViewToggleDecision(bool closeView, bool openView)
        {
            CloseView = closeView;
            OpenView = openView;
        }

        public bool CloseView { get; }
        public bool OpenView { get; }
    }

    public static class UIManagerViewTogglePolicy
    {
        public static UIManagerViewToggleDecision Resolve(bool isOpen)
        {
            return isOpen
                ? new UIManagerViewToggleDecision(true, false)
                : new UIManagerViewToggleDecision(false, true);
        }
    }
}
