namespace ZeroEngine.UI
{
    public readonly struct UIManagerPrefabHandlesReleaseDecision
    {
        public UIManagerPrefabHandlesReleaseDecision(bool releaseHandle)
        {
            ReleaseHandle = releaseHandle;
        }

        public bool ReleaseHandle { get; }
    }

    public static class UIManagerPrefabHandlesReleasePolicy
    {
        public static UIManagerPrefabHandlesReleaseDecision Resolve(bool handleIsValid)
        {
            return new UIManagerPrefabHandlesReleaseDecision(handleIsValid);
        }
    }
}
