namespace ZeroEngine.UI
{
    public readonly struct UIManagerPrefabReferenceDecision
    {
        public UIManagerPrefabReferenceDecision(bool loadPrefab)
        {
            LoadPrefab = loadPrefab;
        }

        public bool LoadPrefab { get; }
    }

    public static class UIManagerPrefabReferencePolicy
    {
        public static UIManagerPrefabReferenceDecision Resolve(bool hasPrefabReference, bool runtimeKeyIsValid)
        {
            return new UIManagerPrefabReferenceDecision(hasPrefabReference && runtimeKeyIsValid);
        }
    }
}
