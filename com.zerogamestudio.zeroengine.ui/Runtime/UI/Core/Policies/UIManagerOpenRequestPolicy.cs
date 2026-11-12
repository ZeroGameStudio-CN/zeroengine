namespace ZeroEngine.UI
{
    public readonly struct UIManagerOpenRequestDecision
    {
        public UIManagerOpenRequestDecision(bool returnExistingVisibleSingleton)
        {
            ReturnExistingVisibleSingleton = returnExistingVisibleSingleton;
        }

        public bool ReturnExistingVisibleSingleton { get; }
    }

    public static class UIManagerOpenRequestPolicy
    {
        public static UIManagerOpenRequestDecision Resolve(
            UIShowMode showMode,
            bool hasExistingInstance,
            bool existingInstanceVisible)
        {
            return new UIManagerOpenRequestDecision(
                showMode == UIShowMode.Singleton && hasExistingInstance && existingInstanceVisible);
        }
    }
}
