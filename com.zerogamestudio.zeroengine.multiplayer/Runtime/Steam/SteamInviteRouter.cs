using System;
using Steamworks;

namespace ZeroEngine.Multiplayer.Steam
{
    public sealed class SteamInviteRouter : IDisposable
    {
        private readonly Callback<GameLobbyJoinRequested_t> _joinRequested;
        private bool _disposed;

        public SteamInviteRouter(ISteamRuntime runtime)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);
        }

        public event Action<JoinRequest> JoinRequested;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _joinRequested.Dispose();
        }

        private void OnJoinRequested(GameLobbyJoinRequested_t callback)
        {
            if (_disposed || !callback.m_steamIDLobby.IsValid())
            {
                return;
            }

            PlatformUser sender = new PlatformUser(
                new PlatformUserId(callback.m_steamIDFriend.m_SteamID.ToString()),
                callback.m_steamIDFriend.IsValid()
                    ? SteamFriends.GetFriendPersonaName(callback.m_steamIDFriend)
                    : string.Empty);
            Action<JoinRequest> handler = JoinRequested;
            if (handler != null)
            {
                handler(new JoinRequest(
                    new RoomId(callback.m_steamIDLobby.m_SteamID.ToString()),
                    sender));
            }
        }
    }
}
