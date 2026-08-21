namespace ZeroEngine.UI
{
    public readonly struct UIManagerModalSideEffectDecision
    {
        public UIManagerModalSideEffectDecision(bool showMask, bool pauseGame, bool hideMask, bool resumeGame)
        {
            ShowMask = showMask;
            PauseGame = pauseGame;
            HideMask = hideMask;
            ResumeGame = resumeGame;
        }

        public bool ShowMask { get; }
        public bool PauseGame { get; }
        public bool HideMask { get; }
        public bool ResumeGame { get; }
    }

    public static class UIManagerModalSideEffectPolicy
    {
        public static UIManagerModalSideEffectDecision Resolve(bool showMask, bool pauseGame)
        {
            return new UIManagerModalSideEffectDecision(showMask, pauseGame, showMask, pauseGame);
        }
    }
}
