namespace ZeroEngine.Multiplayer.Steam
{
    public interface ISteamRuntime
    {
        bool IsAvailable { get; }
        string UnavailableReasonKey { get; }
        PlatformUser LocalUser { get; }
        OperationResult EnsureInitialized();
    }
}
