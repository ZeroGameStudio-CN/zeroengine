namespace ZeroEngine.UI
{
    public readonly struct UIManagerExistingPrefabHandleDecision
    {
        public UIManagerExistingPrefabHandleDecision(bool awaitExistingHandle, bool useExistingPrefab)
        {
            AwaitExistingHandle = awaitExistingHandle;
            UseExistingPrefab = useExistingPrefab;
        }

        public bool AwaitExistingHandle { get; }
        public bool UseExistingPrefab { get; }
    }

    public static class UIManagerExistingPrefabHandlePolicy
    {
        public static UIManagerExistingPrefabHandleDecision Resolve(
            bool handleIsValid,
            bool loadSucceeded,
            bool hasPrefabResult)
        {
            return new UIManagerExistingPrefabHandleDecision(
                handleIsValid,
                handleIsValid && loadSucceeded && hasPrefabResult);
        }
    }
}
