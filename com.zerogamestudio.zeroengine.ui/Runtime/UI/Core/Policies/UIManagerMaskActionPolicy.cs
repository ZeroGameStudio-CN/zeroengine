namespace ZeroEngine.UI
{
    public readonly struct UIManagerMaskActionDecision
    {
        public UIManagerMaskActionDecision(
            bool useMask,
            bool createMask,
            bool positionMask,
            bool applyColor,
            bool clearClickListeners,
            bool addClickListener,
            bool activateMask)
        {
            UseMask = useMask;
            CreateMask = createMask;
            PositionMask = positionMask;
            ApplyColor = applyColor;
            ClearClickListeners = clearClickListeners;
            AddClickListener = addClickListener;
            ActivateMask = activateMask;
        }

        public bool UseMask { get; }
        public bool CreateMask { get; }
        public bool PositionMask { get; }
        public bool ApplyColor { get; }
        public bool ClearClickListeners { get; }
        public bool AddClickListener { get; }
        public bool ActivateMask { get; }
    }

    public static class UIManagerMaskActionPolicy
    {
        public static UIManagerMaskActionDecision Resolve(
            bool hasMaskPrefab,
            bool hasExistingMask,
            bool hasImage,
            bool hasButton,
            bool hasClickAction)
        {
            return new UIManagerMaskActionDecision(
                hasMaskPrefab,
                hasMaskPrefab && !hasExistingMask,
                hasMaskPrefab,
                hasMaskPrefab && hasImage,
                hasMaskPrefab && hasButton,
                hasMaskPrefab && hasButton && hasClickAction,
                hasMaskPrefab);
        }
    }
}
