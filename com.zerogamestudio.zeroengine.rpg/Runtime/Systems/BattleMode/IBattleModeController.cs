namespace ZeroEngine.RPG.TurnBased
{
    public interface IBattleModeController
    {
        BattleMode Mode { get; }
        bool IsInitialized { get; }
        void Cleanup();
    }
}
