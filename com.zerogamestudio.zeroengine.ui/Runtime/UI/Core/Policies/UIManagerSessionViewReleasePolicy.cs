namespace ZeroEngine.UI
{
    public readonly struct UIManagerSessionViewReleaseDecision
    {
        public UIManagerSessionViewReleaseDecision(
            bool releaseView,
            bool removeInstance,
            bool removeFromStack,
            bool hideMask,
            bool destroyInstance,
            bool releasePrefabHandle)
        {
            ReleaseView = releaseView;
            RemoveInstance = removeInstance;
            RemoveFromStack = removeFromStack;
            HideMask = hideMask;
            DestroyInstance = destroyInstance;
            ReleasePrefabHandle = releasePrefabHandle;
        }

        public bool ReleaseView { get; }
        public bool RemoveInstance { get; }
        public bool RemoveFromStack { get; }
        public bool HideMask { get; }
        public bool DestroyInstance { get; }
        public bool ReleasePrefabHandle { get; }
    }

    public static class UIManagerSessionViewReleasePolicy
    {
        public static UIManagerSessionViewReleaseDecision Resolve(bool hasView, bool isResident, bool showMask)
        {
            if (!hasView || isResident)
            {
                return default;
            }

            return new UIManagerSessionViewReleaseDecision(true, true, true, showMask, true, true);
        }
    }
}
