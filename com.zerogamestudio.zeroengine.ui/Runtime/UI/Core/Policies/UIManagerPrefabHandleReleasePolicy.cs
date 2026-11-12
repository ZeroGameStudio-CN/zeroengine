namespace ZeroEngine.UI
{
    public readonly struct UIManagerPrefabHandleReleaseDecision
    {
        public UIManagerPrefabHandleReleaseDecision(
            bool removeViewHandleKey,
            bool removeCachedHandle,
            bool releaseCachedHandle)
        {
            RemoveViewHandleKey = removeViewHandleKey;
            RemoveCachedHandle = removeCachedHandle;
            ReleaseCachedHandle = releaseCachedHandle;
        }

        public bool RemoveViewHandleKey { get; }
        public bool RemoveCachedHandle { get; }
        public bool ReleaseCachedHandle { get; }
    }

    public static class UIManagerPrefabHandleReleasePolicy
    {
        public static UIManagerPrefabHandleReleaseDecision Resolve(
            bool hasViewHandleKey,
            bool handleUsedByOtherView,
            bool hasCachedHandle,
            bool cachedHandleIsValid)
        {
            if (!hasViewHandleKey)
            {
                return default;
            }

            if (handleUsedByOtherView || !hasCachedHandle)
            {
                return new UIManagerPrefabHandleReleaseDecision(true, false, false);
            }

            return new UIManagerPrefabHandleReleaseDecision(true, true, cachedHandleIsValid);
        }
    }
}
