namespace ZeroEngine.UI
{
    public readonly struct UIManagerPrefabLoadResultDecision
    {
        public UIManagerPrefabLoadResultDecision(
            bool cacheLoadedHandle,
            bool markLoadSucceeded,
            bool useLoadedPrefab)
        {
            CacheLoadedHandle = cacheLoadedHandle;
            MarkLoadSucceeded = markLoadSucceeded;
            UseLoadedPrefab = useLoadedPrefab;
        }

        public bool CacheLoadedHandle { get; }
        public bool MarkLoadSucceeded { get; }
        public bool UseLoadedPrefab { get; }
    }

    public static class UIManagerPrefabLoadResultPolicy
    {
        public static UIManagerPrefabLoadResultDecision Resolve(bool loadSucceeded)
        {
            return loadSucceeded
                ? new UIManagerPrefabLoadResultDecision(true, true, true)
                : default;
        }
    }
}
