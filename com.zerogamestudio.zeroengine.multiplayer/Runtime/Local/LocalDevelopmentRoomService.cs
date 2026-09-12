using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.Multiplayer.Local
{
    public sealed class LocalDevelopmentRoomService :
        IPlatformRoomService,
        IRoomStatePublisher,
        IRemoteConnectionAuthorizer
    {
        private readonly LocalDevelopmentRoomOptions _options;
        private readonly List<RoomMember> _members = new List<RoomMember>();
        private INetworkConnectionDriver _driver;
        private RoomSnapshot _currentRoom;
        private bool _disposed;

        public LocalDevelopmentRoomService(LocalDevelopmentRoomOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public bool IsAvailable => !_disposed && _options.Validate().Succeeded;
        public string UnavailableReasonKey => IsAvailable ? string.Empty : "multiplayer.local.unavailable";
        public PlatformUser LocalUser => _options.LocalUser;
        public RoomSnapshot CurrentRoom => _currentRoom;

        public event Action<PlatformRoomEvent> RoomEvent;
        public event Action<JoinRequest> JoinRequested;

        public void AttachConnectionDriver(INetworkConnectionDriver driver)
        {
            ThrowIfDisposed();
            if (ReferenceEquals(_driver, driver))
            {
                return;
            }

            if (_driver != null)
            {
                _driver.ConnectionEvent -= OnConnectionEvent;
            }

            _driver = driver;
            if (_driver != null)
            {
                _driver.ConnectionEvent += OnConnectionEvent;
            }
        }

        public Task<OperationResult<RoomSnapshot>> CreateAsync(
            RoomCreateOptions options,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult(CancelledRoom());
            }

            if (_options.Role != LocalMultiplayerRole.Host)
            {
                return Task.FromResult(OperationResult<RoomSnapshot>.Failure(
                    MultiplayerErrorCode.InvalidState,
                    "multiplayer.local.create_requires_host_role"));
            }

            OperationResult availability = _options.Validate();
            if (!availability.Succeeded)
            {
                return Task.FromResult(OperationResult<RoomSnapshot>.FromFailure(availability));
            }

            _members.Clear();
            _members.Add(new RoomMember(_options.LocalUser, MemberConnectionPhase.LobbyOnly, true, 0));
            _currentRoom = CreateSnapshot(
                options.MaxMembers,
                options.Visibility,
                true,
                options.ProtocolVersion,
                options.Compatibility);
            Raise(RoomEventType.RoomUpdated, default(PlatformUserId));
            return Task.FromResult(OperationResult<RoomSnapshot>.Success(_currentRoom));
        }

        public Task<OperationResult<RoomSnapshot>> JoinAsync(
            RoomId roomId,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult(CancelledRoom());
            }

            if (_options.Role != LocalMultiplayerRole.Client)
            {
                return Task.FromResult(OperationResult<RoomSnapshot>.Failure(
                    MultiplayerErrorCode.InvalidState,
                    "multiplayer.local.join_requires_client_role"));
            }

            if (roomId != _options.RoomId)
            {
                return Task.FromResult(OperationResult<RoomSnapshot>.Failure(
                    MultiplayerErrorCode.RoomNotFound,
                    "multiplayer.local.room_not_found",
                    roomId.Value));
            }

            OperationResult availability = _options.Validate();
            if (!availability.Succeeded)
            {
                return Task.FromResult(OperationResult<RoomSnapshot>.FromFailure(availability));
            }

            _members.Clear();
            _members.Add(new RoomMember(_options.HostUser, MemberConnectionPhase.LobbyOnly, true, 0));
            if (_options.LocalUser.Id != _options.HostUser.Id)
            {
                _members.Add(new RoomMember(_options.LocalUser, MemberConnectionPhase.LobbyOnly, false, 1));
            }

            _currentRoom = CreateSnapshot(
                _options.MaxMembers,
                _options.Visibility,
                true,
                _options.ProtocolVersion,
                _options.Compatibility);
            Raise(RoomEventType.RoomUpdated, default(PlatformUserId));
            return Task.FromResult(OperationResult<RoomSnapshot>.Success(_currentRoom));
        }

        public Task<OperationResult> LeaveAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult(OperationResult.Failure(
                    MultiplayerErrorCode.Cancelled,
                    "multiplayer.error.cancelled"));
            }

            RoomSnapshot previous = _currentRoom;
            _currentRoom = null;
            _members.Clear();
            if (previous != null)
            {
                Action<PlatformRoomEvent> handler = RoomEvent;
                if (handler != null)
                {
                    handler(new PlatformRoomEvent(
                        RoomEventType.Closed,
                        previous.Id,
                        previous.SessionGeneration,
                        null,
                        default(PlatformUserId)));
                }
            }

            return Task.FromResult(OperationResult.Success());
        }

        public OperationResult OpenInviteOverlay()
        {
            ThrowIfDisposed();
            return OperationResult.Failure(
                MultiplayerErrorCode.PlatformUnavailable,
                "multiplayer.local.invite_overlay_unavailable");
        }

        public Task<OperationResult<RoomSnapshot>> RefreshAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult(CancelledRoom());
            }

            return Task.FromResult(_currentRoom == null
                ? OperationResult<RoomSnapshot>.Failure(
                    MultiplayerErrorCode.RoomNotFound,
                    "multiplayer.local.no_current_room")
                : OperationResult<RoomSnapshot>.Success(_currentRoom));
        }

        public OperationResult PublishRoomState(RoomSnapshot room)
        {
            ThrowIfDisposed();
            if (room == null || _currentRoom == null || room.Id != _currentRoom.Id ||
                !_currentRoom.IsHost(LocalUser.Id))
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.InvalidState,
                    "multiplayer.local.state_update_requires_host");
            }

            _members.Clear();
            for (int i = 0; i < room.Members.Count; i++)
            {
                _members.Add(room.Members[i]);
            }

            _currentRoom = room;
            return OperationResult.Success();
        }

        public OperationResult AuthorizeRemoteUser(PlatformUserId userId)
        {
            ThrowIfDisposed();
            if (_options.Role != LocalMultiplayerRole.Host || _currentRoom == null)
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.InvalidState,
                    "multiplayer.local.authorization_requires_host_room");
            }

            if (userId.IsEmpty || userId != _options.ExpectedRemoteUser.Id)
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.UnauthorizedPeer,
                    "multiplayer.local.remote_identity_rejected");
            }

            return OperationResult.Success();
        }

        public void SimulateJoinRequest(PlatformUser sender)
        {
            ThrowIfDisposed();
            Action<JoinRequest> handler = JoinRequested;
            if (handler != null)
            {
                handler(new JoinRequest(_options.RoomId, sender));
            }
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return default(ValueTask);
            }

            _disposed = true;
            if (_driver != null)
            {
                _driver.ConnectionEvent -= OnConnectionEvent;
                _driver = null;
            }

            _currentRoom = null;
            _members.Clear();
            return default(ValueTask);
        }

        private RoomSnapshot CreateSnapshot(
            int maxMembers,
            RoomVisibility visibility,
            bool joinable,
            string protocolVersion,
            CompatibilityDescriptor compatibility)
        {
            return new RoomSnapshot(
                _options.RoomId,
                _options.HostUser.Id,
                _members,
                maxMembers,
                visibility,
                joinable,
                compatibility.ProductId,
                protocolVersion,
                compatibility.GameProtocolVersion,
                compatibility.ContentRevision,
                compatibility.BuildVersion,
                compatibility.GameRoomId,
                _options.SessionId,
                _options.SessionGeneration,
                SessionPhase.InRoom,
                false,
                _options.Address);
        }

        private void OnConnectionEvent(ConnectionEvent connectionEvent)
        {
            if (_currentRoom == null)
            {
                return;
            }

            if (connectionEvent.Type == ConnectionEventType.PeerConnected)
            {
                AddOrUpdateRemoteMember(connectionEvent.UserId, MemberConnectionPhase.Connected);
            }
            else if (connectionEvent.Type == ConnectionEventType.PeerDisconnected)
            {
                AddOrUpdateRemoteMember(connectionEvent.UserId, MemberConnectionPhase.Disconnected);
                Raise(RoomEventType.MemberLeft, connectionEvent.UserId);
            }
            else if (connectionEvent.Type == ConnectionEventType.ClientConnected)
            {
                SetMemberPhase(_options.LocalUser.Id, MemberConnectionPhase.Connected);
                Raise(RoomEventType.RoomUpdated, _options.LocalUser.Id);
            }
        }

        private void AddOrUpdateRemoteMember(PlatformUserId userId, MemberConnectionPhase phase)
        {
            if (userId.IsEmpty)
            {
                return;
            }

            int index = FindMember(userId);
            bool added = index < 0;
            if (added)
            {
                PlatformUser remote = userId == _options.ExpectedRemoteUser.Id
                    ? _options.ExpectedRemoteUser
                    : new PlatformUser(userId, userId.Value);
                _members.Add(new RoomMember(remote, phase, false, _members.Count));
            }
            else
            {
                _members[index] = _members[index].WithConnectionPhase(phase);
            }

            RebuildSnapshot();
            Raise(added ? RoomEventType.MemberJoined : RoomEventType.RoomUpdated, userId);
        }

        private void SetMemberPhase(PlatformUserId userId, MemberConnectionPhase phase)
        {
            int index = FindMember(userId);
            if (index >= 0)
            {
                _members[index] = _members[index].WithConnectionPhase(phase);
                RebuildSnapshot();
            }
        }

        private int FindMember(PlatformUserId userId)
        {
            for (int i = 0; i < _members.Count; i++)
            {
                if (_members[i].User.Id == userId)
                {
                    return i;
                }
            }

            return -1;
        }

        private void RebuildSnapshot()
        {
            RoomSnapshot room = _currentRoom;
            _currentRoom = new RoomSnapshot(
                room.Id,
                room.HostId,
                _members,
                room.MaxMembers,
                room.Visibility,
                room.IsJoinable,
                room.ProductId,
                room.ProtocolVersion,
                room.GameProtocolVersion,
                room.ContentRevision,
                room.BuildVersion,
                room.GameRoomId,
                room.SessionId,
                room.SessionGeneration,
                room.Phase,
                room.HasStarted,
                room.ConnectionAddress);
        }

        private void Raise(RoomEventType type, PlatformUserId memberId)
        {
            Action<PlatformRoomEvent> handler = RoomEvent;
            if (handler != null && _currentRoom != null)
            {
                handler(new PlatformRoomEvent(
                    type,
                    _currentRoom.Id,
                    _currentRoom.SessionGeneration,
                    _currentRoom,
                    memberId));
            }
        }

        private static OperationResult<RoomSnapshot> CancelledRoom()
        {
            return OperationResult<RoomSnapshot>.Failure(
                MultiplayerErrorCode.Cancelled,
                "multiplayer.error.cancelled");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LocalDevelopmentRoomService));
            }
        }
    }
}
