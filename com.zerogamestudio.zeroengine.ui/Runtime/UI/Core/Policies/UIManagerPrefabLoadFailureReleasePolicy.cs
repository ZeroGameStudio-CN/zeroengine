namespace ZeroEngine.UI
{
    public readonly struct UIManagerPrefabLoadFailureReleaseDecision
    {
        public UIManagerPrefabLoadFailureReleaseDecision(bool releaseHandle)
        {
            ReleaseHandle = releaseHandle;
        }

        public bool ReleaseHandle { get; }
    }

    public static class UIManagerPrefabLoadFailureReleasePolicy
    {
        public static UIManagerPrefabLoadFailureReleaseDecision Resolve(bool handleIsValid)
        {
            return new UIManagerPrefabLoadFailureReleaseDecision(handleIsValid);
        }
    }
}
