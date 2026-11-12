using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Steamworks;

namespace ZeroEngine.Multiplayer.Steam
{
    public sealed class SteamRoomService :
        IPlatformRoomService,
        IRoomStatePublisher,
        IRemoteConnectionAuthorizer
    {
        private readonly ISteamRuntime _runtime;
        private readonly string _metadataPrefix;
        private readonly CallResult<LobbyCreated_t> _lobbyCreated;
        private readonly CallResult<LobbyEnter_t> _lobbyEntered;
        private readonly Callback<LobbyChatUpdate_t> _lobbyChatUpdated;
        private readonly Callback<LobbyDataUpdate_t> _lobbyDataUpdated;
        private readonly SteamInviteRouter _inviteRouter;

        private TaskCompletionSource<OperationResult<RoomSnapshot>> _createCompletion;
        private TaskCompletionSource<OperationResult<RoomSnapshot>> _joinCompletion;
        private TaskCompletionSource<OperationResult<RoomSnapshot>> _refreshCompletion;
        private RoomCreateOptions _pendingCreateOptions;
        private SessionId _pendingSessionId;
        private RoomSnapshot _currentRoom;
        private CSteamID _currentLobby = CSteamID.Nil;
        private bool _createCallInFlight;
        private bool _joinCallInFlight;
        private bool _refreshInFlight;
        private bool _disposed;

        public SteamRoomService(ISteamRuntime runtime, string metadataPrefix)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _metadataPrefix = SteamRoomMetadata.NormalizePrefix(metadataPrefix);

            _lobbyCreated = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            _lobbyEntered = CallResult<LobbyEnter_t>.Create(OnLobbyEntered);
            _lobbyChatUpdated = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdated);
            _lobbyDataUpdated = Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdated);
            _inviteRouter = new SteamInviteRouter(_runtime);
            _inviteRouter.JoinRequested += OnJoinRequested;
        }

        public bool IsAvailable => !_disposed && _runtime.IsAvailable;
        public string UnavailableReasonKey => IsAvailable ? string.Empty : _runtime.UnavailableReasonKey;
        public PlatformUser LocalUser => _runtime.LocalUser;
        public RoomSnapshot CurrentRoom => _currentRoom;

        public event Action<PlatformRoomEvent> RoomEvent;
        public event Action<JoinRequest> JoinRequested;

        public async Task<OperationResult<RoomSnapshot>> CreateAsync(
            RoomCreateOptions options,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (cancellationToken.IsCancellationRequested)
            {
                return CancelledRoom();
            }

            OperationResult available = EnsureAvailable();
            if (!available.Succeeded)
            {
                return OperationResult<RoomSnapshot>.FromFailure(available);
            }

            if (HasPendingOperation())
            {
                return OperationResult<RoomSnapshot>.Failure(
                    MultiplayerErrorCode.Busy,
                    "multiplayer.steam.operation_busy");
            }

            _pendingSessionId = new SessionId(Guid.NewGuid().ToString("N"));
            IReadOnlyDictionary<string, string> metadata = SteamRoomMetadata.Create(
                _metadataPrefix,
                options,
                _pendingSessionId,
                1,
                LocalUser.Id);
            OperationResult metadataValidation = SteamRoomMetadata.ValidateValues(metadata);
            if (!metadataValidation.Succeeded)
            {
                return OperationResult<RoomSnapshot>.FromFailure(metadataValidation);
            }

            _pendingCreateOptions = options;
            _createCompletion = NewCompletion();
            _createCallInFlight = true;
            try
            {
                _lobbyCreated.Set(SteamMatchmaking.CreateLobby(
                    ToLobbyType(options.Visibility),
                    options.MaxMembers));
            }
            catch
            {
                _createCompletion = null;
                _createCallInFlight = false;
                throw;
            }

            Task<OperationResult<RoomSnapshot>> task = _createCompletion.Task;
            using (cancellationToken.Register(() =>
                   _createCompletion?.TrySetResult(CancelledRoom())))
            {
                OperationResult<RoomSnapshot> result = await task;
                _createCompletion = null;
                return result;
            }
        }

        public async Task<OperationResult<RoomSnapshot>> JoinAsync(
            RoomId roomId,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (cancellationToken.IsCancellationRequested)
            {
                return CancelledRoom();
            }

            OperationResult available = EnsureAvailable();
            if (!available.Succeeded)
            {
                return OperationResult<RoomSnapshot>.FromFailure(available);
            }

            if (HasPendingOperation())
            {
                return OperationResult<RoomSnapshot>.Failure(
                    MultiplayerErrorCode.Busy,
                    "multiplayer.steam.operation_busy");
            }

            ulong lobbyValue;
            if (roomId.IsEmpty || !ulong.TryParse(roomId.Value, out lobbyValue) || lobbyValue == 0)
            {
                return OperationResult<RoomSnapshot>.Failure(
                    MultiplayerErrorCode.InvalidArgument,
                    "multiplayer.steam.room_id_invalid",
                    roomId.Value);
            }

            CSteamID lobby = new CSteamID(lobbyValue);
            if (!lobby.IsValid())
            {
                return OperationResult<RoomSnapshot>.Failure(
                    MultiplayerErrorCode.InvalidArgument,
                    "multiplayer.steam.room_id_invalid",
                    roomId.Value);
            }

            _joinCompletion = NewCompletion();
            _joinCallInFlight = true;
            try
            {
                _lobbyEntered.Set(SteamMatchmaking.JoinLobby(lobby));
            }
            catch
            {
                _joinCompletion = null;
                _joinCallInFlight = false;
                throw;
            }
            Task<OperationResult<RoomSnapshot>> task = _joinCompletion.Task;
            using (cancellationToken.Register(() =>
                   _joinCompletion?.TrySetResult(CancelledRoom())))
            {
                OperationResult<RoomSnapshot> result = await task;
                _joinCompletion = null;
                return result;
            }
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

            if (HasPendingOperation())
            {
                return Task.FromResult(OperationResult.Failure(
                    MultiplayerErrorCode.Busy,
                    "multiplayer.steam.operation_busy"));
            }

            RoomSnapshot previous = _currentRoom;
            OperationResult available = EnsureAvailable();
            if (!available.Succeeded)
            {
                _currentLobby = CSteamID.Nil;
                _currentRoom = null;
                return Task.FromResult(available);
            }

            if (_currentLobby.IsValid())
            {
                if (SteamMatchmaking.GetLobbyOwner(_currentLobby).m_SteamID == ParseUserId(LocalUser.Id))
                {
                    SteamMatchmaking.SetLobbyData(_currentLobby, Key("state"), "closed");
                    SteamMatchmaking.SetLobbyData(_currentLobby, Key("joinable"), "0");
                    SteamMatchmaking.SetLobbyJoinable(_currentLobby, false);
                }

                SteamMatchmaking.LeaveLobby(_currentLobby);
            }

            _currentLobby = CSteamID.Nil;
            _currentRoom = null;
            if (previous != null)
            {
                Raise(RoomEventType.Closed, previous, default(PlatformUserId));
            }

            return Task.FromResult(OperationResult.Success());
        }

        public OperationResult OpenInviteOverlay()
        {
            ThrowIfDisposed();
            OperationResult available = EnsureAvailable();
            if (!available.Succeeded)
            {
                return available;
            }

            if (!_currentLobby.IsValid())
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.InvalidState,
                    "multiplayer.steam.invite_requires_room");
            }

            SteamFriends.ActivateGameOverlayInviteDialog(_currentLobby);
            return OperationResult.Success();
        }

        public async Task<OperationResult<RoomSnapshot>> RefreshAsync(
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (cancellationToken.IsCancellationRequested)
            {
                return CancelledRoom();
            }

            OperationResult available = EnsureAvailable();
            if (!available.Succeeded)
            {
                return OperationResult<RoomSnapshot>.FromFailure(available);
            }

            if (!_currentLobby.IsValid())
            {
                return OperationResult<RoomSnapshot>.Failure(
                    MultiplayerErrorCode.RoomNotFound,
                    "multiplayer.steam.no_current_room");
            }

            if (HasPendingOperation())
            {
                return OperationResult<RoomSnapshot>.Failure(
                    MultiplayerErrorCode.Busy,
                    "multiplayer.steam.refresh_busy");
            }

            _refreshCompletion = NewCompletion();
            _refreshInFlight = true;
            bool requested;
            try
            {
                requested = SteamMatchmaking.RequestLobbyData(_currentLobby);
            }
            catch
            {
                _refreshCompletion = null;
                _refreshInFlight = false;
                throw;
            }

            if (!requested)
            {
                _refreshCompletion = null;
                _refreshInFlight = false;
                return OperationResult<RoomSnapshot>.Failure(
                    MultiplayerErrorCode.RoomNotFound,
                    "multiplayer.steam.refresh_rejected");
            }

            Task<OperationResult<RoomSnapshot>> task = _refreshCompletion.Task;
            using (cancellationToken.Register(() =>
                   _refreshCompletion?.TrySetResult(CancelledRoom())))
            {
                OperationResult<RoomSnapshot> result = await task;
                _refreshCompletion = null;
                return result;
            }
        }

        public OperationResult PublishRoomState(RoomSnapshot room)
        {
            ThrowIfDisposed();
            OperationResult available = EnsureAvailable();
            if (!available.Succeeded)
            {
                return available;
            }

            if (room == null || !_currentLobby.IsValid() || _currentRoom == null ||
                room.Id != _currentRoom.Id ||
                SteamMatchmaking.GetLobbyOwner(_currentLobby).m_SteamID != ParseUserId(LocalUser.Id))
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.InvalidState,
                    "multiplayer.steam.state_update_requires_host");
            }

            string state = ToMetadataState(room.Phase);
            bool generationWritten = SteamMatchmaking.SetLobbyData(
                _currentLobby,
                Key("generation"),
                room.SessionGeneration.ToString(CultureInfo.InvariantCulture));
            bool stateWritten = SteamMatchmaking.SetLobbyData(_currentLobby, Key("state"), state);
            bool joinableWritten = SteamMatchmaking.SetLobbyData(
                _currentLobby,
                Key("joinable"),
                room.IsJoinable ? "1" : "0");
            bool lobbyUpdated = SteamMatchmaking.SetLobbyJoinable(_currentLobby, room.IsJoinable);
            if (!generationWritten || !stateWritten || !joinableWritten || !lobbyUpdated)
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.CreateFailed,
                    "multiplayer.steam.state_update_failed");
            }

            _currentRoom = room;
            return OperationResult.Success();
        }

        public OperationResult AuthorizeRemoteUser(PlatformUserId userId)
        {
            ThrowIfDisposed();
            OperationResult available = EnsureAvailable();
            if (!available.Succeeded)
            {
                return available;
            }

            if (userId.IsEmpty || !_currentLobby.IsValid() || _currentRoom == null ||
                !_currentRoom.IsHost(LocalUser.Id))
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.UnauthorizedPeer,
                    "multiplayer.steam.authorization_requires_host_room");
            }

            bool wasKnown = false;
            bool wasDisconnected = false;
            for (int i = 0; i < _currentRoom.Members.Count; i++)
            {
                RoomMember member = _currentRoom.Members[i];
                if (member.User.Id == userId)
                {
                    wasKnown = true;
                    wasDisconnected = member.ConnectionPhase == MemberConnectionPhase.Disconnected;
                    break;
                }
            }

            OperationResult<RoomSnapshot> current = BuildSnapshot(_currentLobby);
            if (!current.Succeeded)
            {
                return current.Result;
            }

            RoomSnapshot snapshot = current.Value;
            bool isMember = false;
            for (int i = 0; i < snapshot.Members.Count; i++)
            {
                if (snapshot.Members[i].User.Id == userId)
                {
                    isMember = true;
                    break;
                }
            }

            if (!isMember)
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.UnauthorizedPeer,
                    "multiplayer.steam.remote_not_in_lobby");
            }

            if (snapshot.Members.Count > snapshot.MaxMembers)
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.RoomFull,
                    "multiplayer.steam.room_over_capacity");
            }

            if (!snapshot.IsJoinable && !wasDisconnected)
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.RoomStarted,
                    "multiplayer.steam.room_not_joinable");
            }

            _currentRoom = snapshot;
            Raise(
                wasKnown ? RoomEventType.RoomUpdated : RoomEventType.MemberJoined,
                snapshot,
                userId);
            return OperationResult.Success();
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return default(ValueTask);
            }

            if (_currentLobby.IsValid() && _runtime.IsAvailable)
            {
                try
                {
                    SteamMatchmaking.LeaveLobby(_currentLobby);
                }
                catch
                {
                    // Disposal must not throw after the Steam runtime has begun shutting down.
                }
            }

            _disposed = true;
            _createCompletion?.TrySetResult(CancelledRoom());
            _joinCompletion?.TrySetResult(CancelledRoom());
            _refreshCompletion?.TrySetResult(CancelledRoom());
            _createCompletion = null;
            _joinCompletion = null;
            _refreshCompletion = null;
            _createCallInFlight = false;
            _joinCallInFlight = false;
            _refreshInFlight = false;
            _currentLobby = CSteamID.Nil;
            _currentRoom = null;
            _inviteRouter.JoinRequested -= OnJoinRequested;
            _inviteRouter.Dispose();
            _lobbyCreated.Dispose();
            _lobbyEntered.Dispose();
            _lobbyChatUpdated.Dispose();
            _lobbyDataUpdated.Dispose();
            return default(ValueTask);
        }

        private void OnLobbyCreated(LobbyCreated_t callback, bool ioFailure)
        {
            try
            {
                TaskCompletionSource<OperationResult<RoomSnapshot>> completion = _createCompletion;
                CSteamID lobby = new CSteamID(callback.m_ulSteamIDLobby);
                if (completion == null || completion.Task.IsCompleted)
                {
                    if (!ioFailure && callback.m_eResult == EResult.k_EResultOK && lobby.IsValid())
                    {
                        SteamMatchmaking.LeaveLobby(lobby);
                    }

                    return;
                }

                if (ioFailure || callback.m_eResult != EResult.k_EResultOK || !lobby.IsValid())
                {
                    completion.TrySetResult(OperationResult<RoomSnapshot>.Failure(
                        MultiplayerErrorCode.CreateFailed,
                        "multiplayer.steam.create_failed",
                        callback.m_eResult.ToString()));
                    return;
                }

                IReadOnlyDictionary<string, string> metadata = SteamRoomMetadata.Create(
                    _metadataPrefix,
                    _pendingCreateOptions,
                    _pendingSessionId,
                    1,
                    LocalUser.Id);
                foreach (KeyValuePair<string, string> pair in metadata)
                {
                    if (!SteamMatchmaking.SetLobbyData(lobby, pair.Key, pair.Value))
                    {
                        SteamMatchmaking.LeaveLobby(lobby);
                        completion.TrySetResult(OperationResult<RoomSnapshot>.Failure(
                            MultiplayerErrorCode.CreateFailed,
                            "multiplayer.steam.metadata_write_failed",
                            pair.Key));
                        return;
                    }
                }

                SteamMatchmaking.SetLobbyJoinable(lobby, true);
                _currentLobby = lobby;
                OperationResult<RoomSnapshot> snapshot = BuildSnapshot(lobby);
                if (snapshot.Succeeded)
                {
                    _currentRoom = snapshot.Value;
                    Raise(RoomEventType.RoomUpdated, _currentRoom, default(PlatformUserId));
                }

                completion.TrySetResult(snapshot);
            }
            finally
            {
                _createCallInFlight = false;
            }
        }

        private void OnLobbyEntered(LobbyEnter_t callback, bool ioFailure)
        {
            try
            {
                TaskCompletionSource<OperationResult<RoomSnapshot>> completion = _joinCompletion;
                CSteamID lobby = new CSteamID(callback.m_ulSteamIDLobby);
                EChatRoomEnterResponse response = (EChatRoomEnterResponse)callback.m_EChatRoomEnterResponse;
                if (completion == null || completion.Task.IsCompleted)
                {
                    if (!ioFailure && response == EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess &&
                        lobby.IsValid())
                    {
                        SteamMatchmaking.LeaveLobby(lobby);
                    }

                    return;
                }

                if (ioFailure || response != EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess || !lobby.IsValid())
                {
                    completion.TrySetResult(OperationResult<RoomSnapshot>.Failure(
                        response == EChatRoomEnterResponse.k_EChatRoomEnterResponseFull
                            ? MultiplayerErrorCode.RoomFull
                            : MultiplayerErrorCode.JoinFailed,
                        "multiplayer.steam.join_failed",
                        response.ToString()));
                    return;
                }

                _currentLobby = lobby;
                OperationResult<RoomSnapshot> snapshot = BuildSnapshot(lobby);
                if (!snapshot.Succeeded)
                {
                    SteamMatchmaking.LeaveLobby(lobby);
                    _currentLobby = CSteamID.Nil;
                }
                else
                {
                    _currentRoom = snapshot.Value;
                    Raise(RoomEventType.RoomUpdated, _currentRoom, LocalUser.Id);
                }

                completion.TrySetResult(snapshot);
            }
            finally
            {
                _joinCallInFlight = false;
            }
        }

        private void OnLobbyChatUpdated(LobbyChatUpdate_t callback)
        {
            if (!_currentLobby.IsValid() || callback.m_ulSteamIDLobby != _currentLobby.m_SteamID)
            {
                return;
            }

            PlatformUserId changed = new PlatformUserId(
                callback.m_ulSteamIDUserChanged.ToString(CultureInfo.InvariantCulture));
            EChatMemberStateChange change = (EChatMemberStateChange)callback.m_rgfChatMemberStateChange;
            bool entered = (change & EChatMemberStateChange.k_EChatMemberStateChangeEntered) != 0;
            bool left = (change & (EChatMemberStateChange.k_EChatMemberStateChangeLeft |
                                   EChatMemberStateChange.k_EChatMemberStateChangeDisconnected |
                                   EChatMemberStateChange.k_EChatMemberStateChangeKicked |
                                   EChatMemberStateChange.k_EChatMemberStateChangeBanned)) != 0;

            RoomSnapshot previous = _currentRoom;
            if (left && previous != null && changed == previous.HostId)
            {
                Raise(RoomEventType.HostLeft, previous, changed);
                return;
            }

            OperationResult<RoomSnapshot> snapshot = BuildSnapshot(_currentLobby);
            if (!snapshot.Succeeded)
            {
                return;
            }

            _currentRoom = snapshot.Value;
            Raise(
                entered ? RoomEventType.MemberJoined : left ? RoomEventType.MemberLeft : RoomEventType.RoomUpdated,
                _currentRoom,
                changed);
        }

        private void OnLobbyDataUpdated(LobbyDataUpdate_t callback)
        {
            if (!_currentLobby.IsValid() || callback.m_ulSteamIDLobby != _currentLobby.m_SteamID)
            {
                return;
            }

            if (callback.m_bSuccess == 0)
            {
                _refreshInFlight = false;
                _refreshCompletion?.TrySetResult(OperationResult<RoomSnapshot>.Failure(
                    MultiplayerErrorCode.RoomNotFound,
                    "multiplayer.steam.refresh_failed"));
                return;
            }

            OperationResult<RoomSnapshot> snapshot = BuildSnapshot(_currentLobby);
            if (!snapshot.Succeeded)
            {
                _refreshInFlight = false;
                _refreshCompletion?.TrySetResult(snapshot);
                return;
            }

            _currentRoom = snapshot.Value;
            _refreshInFlight = false;
            Raise(RoomEventType.DataChanged, _currentRoom, default(PlatformUserId));
            _refreshCompletion?.TrySetResult(snapshot);
        }

        private OperationResult<RoomSnapshot> BuildSnapshot(CSteamID lobby)
        {
            OperationResult<SteamRoomDescriptor> metadata = SteamRoomMetadata.Read(
                _metadataPrefix,
                key => SteamMatchmaking.GetLobbyData(lobby, key));
            if (!metadata.Succeeded)
            {
                return OperationResult<RoomSnapshot>.FromFailure(metadata.Result);
            }

            SteamRoomDescriptor descriptor = metadata.Value;
            CSteamID lobbyOwner = SteamMatchmaking.GetLobbyOwner(lobby);
            if (!lobbyOwner.IsValid() || lobbyOwner.m_SteamID.ToString() != descriptor.HostId.Value)
            {
                return OperationResult<RoomSnapshot>.Failure(
                    MultiplayerErrorCode.HostLeft,
                    "multiplayer.steam.host_identity_mismatch");
            }

            int memberCount = SteamMatchmaking.GetNumLobbyMembers(lobby);
            List<RoomMember> members = new List<RoomMember>(memberCount);
            for (int i = 0; i < memberCount; i++)
            {
                CSteamID memberId = SteamMatchmaking.GetLobbyMemberByIndex(lobby, i);
                PlatformUserId id = new PlatformUserId(memberId.m_SteamID.ToString());
                string displayName = SteamFriends.GetFriendPersonaName(memberId);
                MemberConnectionPhase phase = FindExistingMemberPhase(id);
                members.Add(new RoomMember(
                    new PlatformUser(id, displayName),
                    phase,
                    id == descriptor.HostId,
                    i));
            }

            RoomSnapshot snapshot = new RoomSnapshot(
                new RoomId(lobby.m_SteamID.ToString()),
                descriptor.HostId,
                members,
                SteamMatchmaking.GetLobbyMemberLimit(lobby),
                descriptor.Visibility,
                descriptor.IsJoinable,
                descriptor.ProductId,
                descriptor.ProtocolVersion,
                descriptor.GameProtocolVersion,
                descriptor.ContentRevision,
                descriptor.BuildVersion,
                descriptor.GameRoomId,
                descriptor.SessionId,
                descriptor.SessionGeneration,
                descriptor.Phase,
                descriptor.HasStarted,
                descriptor.HostId.Value);
            return OperationResult<RoomSnapshot>.Success(snapshot);
        }

        private MemberConnectionPhase FindExistingMemberPhase(PlatformUserId userId)
        {
            if (_currentRoom != null)
            {
                for (int i = 0; i < _currentRoom.Members.Count; i++)
                {
                    if (_currentRoom.Members[i].User.Id == userId)
                    {
                        return _currentRoom.Members[i].ConnectionPhase;
                    }
                }
            }

            return MemberConnectionPhase.LobbyOnly;
        }

        private void OnJoinRequested(JoinRequest request)
        {
            Action<JoinRequest> handler = JoinRequested;
            if (handler != null)
            {
                handler(request);
            }
        }

        private OperationResult EnsureAvailable()
        {
            if (!_runtime.IsAvailable)
            {
                OperationResult initialized = _runtime.EnsureInitialized();
                if (!initialized.Succeeded)
                {
                    return initialized;
                }
            }

            return OperationResult.Success();
        }

        private bool HasPendingOperation()
        {
            return _createCompletion != null || _joinCompletion != null || _refreshCompletion != null ||
                   _createCallInFlight || _joinCallInFlight || _refreshInFlight;
        }

        private string Key(string suffix)
        {
            return _metadataPrefix + suffix;
        }

        private void Raise(RoomEventType type, RoomSnapshot snapshot, PlatformUserId memberId)
        {
            Action<PlatformRoomEvent> handler = RoomEvent;
            if (handler != null && snapshot != null)
            {
                handler(new PlatformRoomEvent(
                    type,
                    snapshot.Id,
                    snapshot.SessionGeneration,
                    snapshot,
                    memberId));
            }
        }

        private OperationResult<RoomSnapshot> CancelledRoom()
        {
            return OperationResult<RoomSnapshot>.Failure(
                MultiplayerErrorCode.Cancelled,
                "multiplayer.error.cancelled");
        }

        private static TaskCompletionSource<OperationResult<RoomSnapshot>> NewCompletion()
        {
            return new TaskCompletionSource<OperationResult<RoomSnapshot>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static ELobbyType ToLobbyType(RoomVisibility visibility)
        {
            if (visibility == RoomVisibility.Public)
            {
                return ELobbyType.k_ELobbyTypePublic;
            }

            return visibility == RoomVisibility.FriendsOnly
                ? ELobbyType.k_ELobbyTypeFriendsOnly
                : ELobbyType.k_ELobbyTypePrivate;
        }

        private static string ToMetadataState(SessionPhase phase)
        {
            if (phase == SessionPhase.Connecting || phase == SessionPhase.Synchronizing)
            {
                return "connecting";
            }

            if (phase == SessionPhase.Ready)
            {
                return "ready";
            }

            if (phase == SessionPhase.Starting)
            {
                return "starting";
            }

            if (phase == SessionPhase.InGame)
            {
                return "ingame";
            }

            return phase == SessionPhase.Recovery || phase == SessionPhase.Failed
                ? "closed"
                : "waiting";
        }

        private static ulong ParseUserId(PlatformUserId userId)
        {
            ulong value;
            return ulong.TryParse(userId.Value, out value) ? value : 0;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SteamRoomService));
            }
        }
    }
}
