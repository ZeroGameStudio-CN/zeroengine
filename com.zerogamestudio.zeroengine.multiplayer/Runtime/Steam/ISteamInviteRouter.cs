using System;

namespace ZeroEngine.Multiplayer.Steam
{
    public interface ISteamInviteRouter : IDisposable
    {
        event Action<JoinRequest> JoinRequested;
    }
}
