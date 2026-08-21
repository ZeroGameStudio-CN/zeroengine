namespace ZeroEngine.UI
{
    public readonly struct UIManagerCloseActionDecision
    {
        public UIManagerCloseActionDecision(
            bool removeInstance,
            bool releasePrefabHandle,
            bool destroyInstance,
            bool deactivateInstance)
        {
            RemoveInstance = removeInstance;
            ReleasePrefabHandle = releasePrefabHandle;
            DestroyInstance = destroyInstance;
            DeactivateInstance = deactivateInstance;
        }

        public bool RemoveInstance { get; }
        public bool ReleasePrefabHandle { get; }
        public bool DestroyInstance { get; }
        public bool DeactivateInstance { get; }
    }

    public static class UIManagerCloseActionPolicy
    {
        public static UIManagerCloseActionDecision Resolve(
            UIViewLifetime lifetime,
            UICloseMode closeMode)
        {
            if (lifetime == UIViewLifetime.Evictable)
            {
                return new UIManagerCloseActionDecision(true, true, true, false);
            }

            return closeMode switch
            {
                UICloseMode.Destroy => new UIManagerCloseActionDecision(true, false, true, false),
                UICloseMode.Pool => new UIManagerCloseActionDecision(false, false, false, true),
                _ => default
            };
        }
    }
}
