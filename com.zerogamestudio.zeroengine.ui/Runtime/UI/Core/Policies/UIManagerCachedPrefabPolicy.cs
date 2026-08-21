namespace ZeroEngine.UI
{
    public readonly struct UIManagerCachedPrefabDecision
    {
        public UIManagerCachedPrefabDecision(bool useCachedPrefab)
        {
            UseCachedPrefab = useCachedPrefab;
        }

        public bool UseCachedPrefab { get; }
    }

    public static class UIManagerCachedPrefabPolicy
    {
        public static UIManagerCachedPrefabDecision Resolve(
            bool hasHandle,
            bool handleIsValid,
            bool loadSucceeded,
            bool hasPrefabResult)
        {
            return new UIManagerCachedPrefabDecision(
                hasHandle && handleIsValid && loadSucceeded && hasPrefabResult);
        }
    }
}
