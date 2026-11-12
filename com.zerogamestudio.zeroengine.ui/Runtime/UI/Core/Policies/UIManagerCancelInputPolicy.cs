namespace ZeroEngine.UI
{
    public readonly struct UIManagerCancelInputDecision
    {
        public UIManagerCancelInputDecision(bool closeTopView, bool consumeInput)
        {
            CloseTopView = closeTopView;
            ConsumeInput = consumeInput;
        }

        public bool CloseTopView { get; }
        public bool ConsumeInput { get; }
    }

    public static class UIManagerCancelInputPolicy
    {
        public static UIManagerCancelInputDecision Resolve(
            bool hasTopView,
            bool topViewConsumedInput,
            bool allowEscClose)
        {
            if (!hasTopView)
            {
                return default;
            }

            if (topViewConsumedInput)
            {
                return new UIManagerCancelInputDecision(false, true);
            }

            if (allowEscClose)
            {
                return new UIManagerCancelInputDecision(true, true);
            }

            return default;
        }
    }
}
