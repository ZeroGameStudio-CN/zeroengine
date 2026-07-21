using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.Multiplayer
{
    public interface IMultiplayerSessionSettings
    {
        RoomVisibility DefaultVisibility { get; }
        int MaxPlayers { get; }
        int MinimumPlayersToStart { get; }
        bool AllowJoinInProgress { get; }
        TimeSpan CreateTimeout { get; }
        TimeSpan JoinTimeout { get; }
        TimeSpan ConnectionTimeout { get; }
        TimeSpan InitialSyncTimeout { get; }
        TimeSpan StartTimeout { get; }
        TimeSpan LeaveTimeout { get; }
        bool ReconnectEnabled { get; }
        TimeSpan ReconnectGracePeriod { get; }
        int ReconnectMaxAttempts { get; }
        TimeSpan ReconnectAttemptTimeout { get; }
        IReadOnlyList<TimeSpan> ReconnectRetryIntervals { get; }
        TimeSpan ReconnectHardDeadline { get; }
        string ProtocolVersion { get; }
        BuildMatchPolicy BuildMatchPolicy { get; }
    }

    public interface IMultiplayerSessionSettingsValidator
    {
        IReadOnlyList<string> ValidateAdditionalSettings();
    }

    public interface IPlatformRoomService : IAsyncDisposable
    {
        bool IsAvailable { get; }
        string UnavailableReasonKey { get; }
        PlatformUser LocalUser { get; }
        RoomSnapshot CurrentRoom { get; }

        event Action<PlatformRoomEvent> RoomEvent;
        event Action<JoinRequest> JoinRequested;

        Task<OperationResult<RoomSnapshot>> CreateAsync(
            RoomCreateOptions options,
            CancellationToken cancellationToken);

        Task<OperationResult<RoomSnapshot>> JoinAsync(
            RoomId roomId,
            CancellationToken cancellationToken);

        Task<OperationResult> LeaveAsync(CancellationToken cancellationToken);
        OperationResult OpenInviteOverlay();
        Task<OperationResult<RoomSnapshot>> RefreshAsync(CancellationToken cancellationToken);
    }

    public interface IRoomStatePublisher
    {
        OperationResult PublishRoomState(RoomSnapshot room);
    }

    public interface IRemoteConnectionAuthorizer
    {
        OperationResult AuthorizeRemoteUser(PlatformUserId userId);
    }

    public interface INetworkConnectionDriver
    {
        ConnectionPhase Phase { get; }
        bool IsServer { get; }
        bool IsClient { get; }

        event Action<ConnectionEvent> ConnectionEvent;

        Task<OperationResult> StartHostAsync(
            HostConnectionOptions options,
            CancellationToken cancellationToken);

        Task<OperationResult> StartClientAsync(
            ClientConnectionOptions options,
            CancellationToken cancellationToken);

        Task<OperationResult> StopAsync(
            DisconnectIntent intent,
            CancellationToken cancellationToken);
    }

    public interface IMultiplayerGameAdapter
    {
        CompatibilityDescriptor GetCompatibility();
        CompatibilityDescriptor GetCompatibility(string gameRoomId);

        Task<OperationResult> PrepareSessionAsync(
            MultiplayerSessionContext context,
            CancellationToken cancellationToken);

        Task<OperationResult> SynchronizeLocalAsync(
            MultiplayerSessionContext context,
            CancellationToken cancellationToken);

        Task<OperationResult> SynchronizePeerAsync(
            MultiplayerPeer peer,
            CancellationToken cancellationToken);

        Task<OperationResult> RestorePeerAsync(
            MultiplayerPeer peer,
            CancellationToken cancellationToken);

        void OnPeerDisconnected(MultiplayerPeer peer, TimeSpan gracePeriod);
        void OnPeerReconnectExpired(MultiplayerPeer peer);
        void OnSessionEnded(SessionEndReason reason);
    }
}
