using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.Multiplayer
{
    public sealed class MultiplayerSessionCoordinator : IAsyncDisposable
    {
        private readonly MultiplayerSessionConfig _config;
        private readonly IPlatformRoomService _platform;
        private readonly INetworkConnectionDriver _driver;
        private readonly IMultiplayerGameAdapter _game;
        private readonly MultiplayerSessionStateMachine _stateMachine;
        private readonly OperationGenerationGate _operationGeneration = new OperationGenerationGate();
        private readonly InviteRouter _inviteRouter = new InviteRouter();
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);

        private RoomSnapshot _room;
        private OperationResult _lastResult = OperationResult.Success();
        private RetryOperationKind _retryOperation;
        private RoomId _retryRoomId;
        private bool _operationInProgress;
        private bool _intentionalLeave;
        private bool _disposed;
        private TimeSpan _reconnectRemaining;

        public MultiplayerSessionCoordinator(
            MultiplayerSessionConfig config,
            IPlatformRoomService platform,
            INetworkConnectionDriver driver,
            IMultiplayerGameAdapter game)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _platform = platform ?? throw new ArgumentNullException(nameof(platform));
            _driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _game = game ?? throw new ArgumentNullException(nameof(game));
            _stateMachine = new MultiplayerSessionStateMachine();
            _stateMachine.PhaseChanged += OnPhaseChanged;
            _platform.RoomEvent += OnRoomEvent;
            _platform.JoinRequested += OnJoinRequested;
            _driver.ConnectionEvent += OnConnectionEvent;
        }

        public SessionPhase Phase => _stateMachine.Phase;
        public RoomSnapshot CurrentRoom => _room;
        public int OperationGeneration => _operationGeneration.Current;
        public bool HasPendingInvite => _inviteRouter.HasPending;

        public event Action Changed;
        public event Action<JoinRequest, InviteRouteAction> JoinRequestRouted;

        public MultiplayerSessionSnapshot GetSnapshot()
        {
            return new MultiplayerSessionSnapshot(
                Phase,
                _room,
                _platform.LocalUser,
                _driver.IsServer,
                _operationInProgress,
                _retryOperation,
                _lastResult,
                _reconnectRemaining);
        }

        public Task<OperationResult> InitializeAsync(CancellationToken cancellationToken)
        {
            OperationResult busy;
            if (!TryBeginOperation(out busy))
            {
                return Task.FromResult(busy);
            }

            try
            {
                return Task.FromResult(InitializeCore(cancellationToken));
            }
            finally
            {
                EndOperation();
            }
        }

        public async Task<OperationResult<RoomSnapshot>> CreateRoomAsync(CancellationToken cancellationToken)
        {
            OperationResult busy;
            if (!TryBeginOperation(out busy))
            {
                return OperationResult<RoomSnapshot>.FromFailure(busy);
            }

            try
            {
                int generation = _operationGeneration.Begin();
                _intentionalLeave = false;

                OperationResult transition = Transition(SessionPhase.CreatingRoom);
                if (!transition.Succeeded)
                {
                    return OperationResult<RoomSnapshot>.FromFailure(SetLastResult(transition));
                }

                _room = null;
                _retryOperation = RetryOperationKind.CreateRoom;
                _retryRoomId = default(RoomId);
                CompatibilityDescriptor compatibility;
                try
                {
                    compatibility = _game.GetCompatibility();
                }
                catch (Exception exception)
                {
                    return FailRoomOperation(OperationResult.Failure(
                        MultiplayerErrorCode.InvalidConfiguration,
                        "multiplayer.error.compatibility_provider_failed",
                        exception.GetType().Name));
                }

                OperationResult descriptor = CompatibilityValidator.ValidateDescriptor(
                    compatibility,
                    _config.ProtocolVersion);
                if (!descriptor.Succeeded)
                {
                    return FailRoomOperation(descriptor);
                }

                RoomCreateOptions options = new RoomCreateOptions(
                    _config.DefaultVisibility,
                    _config.MaxPlayers,
                    _config.AllowJoinInProgress,
                    _config.ProtocolVersion,
                    compatibility);

                OperationResult<RoomSnapshot> create = await RunWithTimeout(
                    token => _platform.CreateAsync(options, token),
                    _config.CreateTimeout,
                    cancellationToken,
                    MultiplayerErrorCode.CreateFailed,
                    "multiplayer.error.create_failed");

                if (!IsCurrent(generation))
                {
                    return StaleRoomResult();
                }

                if (!create.Succeeded)
                {
                    return FailRoomOperation(create.Result);
                }

                if (create.Value == null)
                {
                    await CleanupFailedJoinAsync();
                    return FailRoomOperation(OperationResult.Failure(
                        MultiplayerErrorCode.CreateFailed,
                        "multiplayer.error.create_returned_no_room"));
                }

                _room = create.Value;
                transition = Transition(SessionPhase.Connecting);
                if (!transition.Succeeded)
                {
                    return FailRoomOperation(transition);
                }

                OperationResult start = await RunWithTimeout(
                    token => _driver.StartHostAsync(
                        new HostConnectionOptions(_room.Id, _room.SessionId, _room.SessionGeneration),
                        token),
                    _config.ConnectionTimeout,
                    cancellationToken,
                    MultiplayerErrorCode.TransportFailed,
                    "multiplayer.error.host_start_failed");

                if (!IsCurrent(generation))
                {
                    return StaleRoomResult();
                }

                if (!start.Succeeded)
                {
                    await CleanupFailedJoinAsync();
                    return FailRoomOperation(start);
                }

                OperationResult prepare = await RunWithTimeout(
                    token => _game.PrepareSessionAsync(
                        new MultiplayerSessionContext(_room, _platform.LocalUser, true),
                        token),
                    _config.InitialSyncTimeout,
                    cancellationToken,
                    MultiplayerErrorCode.SynchronizationFailed,
                    "multiplayer.error.host_prepare_failed");

                if (!IsCurrent(generation))
                {
                    return StaleRoomResult();
                }

                if (!prepare.Succeeded)
                {
                    await CleanupFailedJoinAsync();
                    return FailRoomOperation(prepare);
                }

                _room = _room
                    .WithMemberPhase(_platform.LocalUser.Id, MemberConnectionPhase.Ready)
                    .WithSession(_room.SessionGeneration, SessionPhase.InRoom, false, true);
                OperationResult publish = PublishCurrentRoomState();
                if (!publish.Succeeded)
                {
                    await CleanupFailedJoinAsync();
                    return FailRoomOperation(publish);
                }

                transition = Transition(SessionPhase.InRoom);
                if (!transition.Succeeded)
                {
                    return FailRoomOperation(transition);
                }

                _retryOperation = RetryOperationKind.None;
                SetLastResult(OperationResult.Success());
                return OperationResult<RoomSnapshot>.Success(_room);
            }
            finally
            {
                EndOperation();
            }
        }

        public async Task<OperationResult<RoomSnapshot>> JoinRoomAsync(
            RoomId roomId,
            CancellationToken cancellationToken)
        {
            if (roomId.IsEmpty)
            {
                return OperationResult<RoomSnapshot>.Failure(
                    MultiplayerErrorCode.InvalidArgument,
                    "multiplayer.error.room_id_missing");
            }

            OperationResult busy;
            if (!TryBeginOperation(out busy))
            {
                return OperationResult<RoomSnapshot>.FromFailure(busy);
            }

            try
            {
                int generation = _operationGeneration.Begin();
                _intentionalLeave = false;

                OperationResult transition = Transition(SessionPhase.JoiningRoom);
                if (!transition.Succeeded)
                {
                    return OperationResult<RoomSnapshot>.FromFailure(SetLastResult(transition));
                }

                _room = null;
                _retryOperation = RetryOperationKind.JoinRoom;
                _retryRoomId = roomId;

                OperationResult<RoomSnapshot> join = await RunWithTimeout(
                    token => _platform.JoinAsync(roomId, token),
                    _config.JoinTimeout,
                    cancellationToken,
                    MultiplayerErrorCode.JoinFailed,
                    "multiplayer.error.join_failed");

                if (!IsCurrent(generation))
                {
                    return StaleRoomResult();
                }

                if (!join.Succeeded)
                {
                    return FailRoomOperation(join.Result);
                }

                if (join.Value == null)
                {
                    await CleanupFailedJoinAsync();
                    return FailRoomOperation(OperationResult.Failure(
                        MultiplayerErrorCode.JoinFailed,
                        "multiplayer.error.join_returned_no_room"));
                }

                _room = join.Value;
                if (!_room.IsJoinable)
                {
                    await CleanupFailedJoinAsync();
                    return FailRoomOperation(OperationResult.Failure(
                        _room.HasStarted || _room.Phase == SessionPhase.Starting ||
                        _room.Phase == SessionPhase.InGame
                            ? MultiplayerErrorCode.RoomStarted
                            : MultiplayerErrorCode.JoinFailed,
                        "multiplayer.error.room_not_joinable"));
                }

                CompatibilityDescriptor localCompatibility;
                try
                {
                    localCompatibility = _game.GetCompatibility();
                }
                catch (Exception exception)
                {
                    await CleanupFailedJoinAsync();
                    return FailRoomOperation(OperationResult.Failure(
                        MultiplayerErrorCode.InvalidConfiguration,
                        "multiplayer.error.compatibility_provider_failed",
                        exception.GetType().Name));
                }

                OperationResult compatibility = CompatibilityValidator.Validate(
                    _room,
                    localCompatibility,
                    _config.ProtocolVersion,
                    _config.BuildMatchPolicy);
                if (!compatibility.Succeeded)
                {
                    await CleanupFailedJoinAsync();
                    return FailRoomOperation(compatibility);
                }

                transition = Transition(SessionPhase.Connecting);
                if (!transition.Succeeded)
                {
                    return FailRoomOperation(transition);
                }

                OperationResult start = await RunWithTimeout(
                    token => _driver.StartClientAsync(
                        new ClientConnectionOptions(
                            _room.Id,
                            _room.HostId,
                            _room.SessionId,
                            _room.SessionGeneration,
                            _room.ConnectionAddress),
                        token),
                    _config.ConnectionTimeout,
                    cancellationToken,
                    MultiplayerErrorCode.TransportFailed,
                    "multiplayer.error.client_start_failed");

                if (!IsCurrent(generation))
                {
                    return StaleRoomResult();
                }

                if (!start.Succeeded)
                {
                    await CleanupFailedJoinAsync();
                    return FailRoomOperation(start);
                }

                Transition(SessionPhase.Synchronizing);
                OperationResult prepare = await RunWithTimeout(
                    token => _game.PrepareSessionAsync(
                        new MultiplayerSessionContext(_room, _platform.LocalUser, false),
                        token),
                    _config.InitialSyncTimeout,
                    cancellationToken,
                    MultiplayerErrorCode.SynchronizationFailed,
                    "multiplayer.error.client_prepare_failed");

                if (!IsCurrent(generation))
                {
                    return StaleRoomResult();
                }

                if (!prepare.Succeeded)
                {
                    await CleanupFailedJoinAsync();
                    return FailRoomOperation(prepare);
                }

                _room = _room.WithMemberPhase(_platform.LocalUser.Id, MemberConnectionPhase.Synchronizing);
                _retryOperation = RetryOperationKind.None;
                SetLastResult(OperationResult.Success());
                return OperationResult<RoomSnapshot>.Success(_room);
            }
            finally
            {
                EndOperation();
            }
        }

        public async Task<OperationResult<RoomSnapshot>> AcceptJoinRequestAsync(CancellationToken cancellationToken)
        {
            if (!_inviteRouter.HasPending)
            {
                return OperationResult<RoomSnapshot>.Failure(
                    MultiplayerErrorCode.InvalidState,
                    "multiplayer.error.no_pending_invite");
            }

            bool mustLeave = Phase == SessionPhase.InRoom || Phase == SessionPhase.Ready ||
                             Phase == SessionPhase.InGame || Phase == SessionPhase.Recovery;
            bool canJoin = Phase == SessionPhase.Idle || Phase == SessionPhase.Failed || mustLeave;
            if (!canJoin)
            {
                return OperationResult<RoomSnapshot>.Failure(
                    MultiplayerErrorCode.InvalidState,
                    "multiplayer.error.invite_not_ready");
            }

            JoinRequest request;
            _inviteRouter.TryTakePending(out request);
            NotifyChanged();

            if (mustLeave)
            {
                OperationResult leave = await LeaveAsync(cancellationToken);
                if (!leave.Succeeded)
                {
                    return OperationResult<RoomSnapshot>.FromFailure(leave);
                }
            }

            return await JoinRoomAsync(request.RoomId, cancellationToken);
        }

        public OperationResult Invite()
        {
            if (_room == null || !_room.IsHost(_platform.LocalUser.Id) ||
                (Phase != SessionPhase.InRoom && Phase != SessionPhase.Ready))
            {
                return SetLastResult(OperationResult.Failure(
                    MultiplayerErrorCode.InvalidState,
                    "multiplayer.error.invite_not_available"));
            }

            return SetLastResult(_platform.OpenInviteOverlay());
        }

        public async Task<OperationResult> SynchronizePeerAsync(
            MultiplayerPeer peer,
            CancellationToken cancellationToken)
        {
            OperationResult busy;
            if (!TryBeginOperation(out busy))
            {
                return busy;
            }

            try
            {
                int generation = _operationGeneration.Begin();
                if (!_driver.IsServer)
                {
                    return SetLastResult(OperationResult.Failure(
                        MultiplayerErrorCode.InvalidState,
                        "multiplayer.error.server_required"));
                }

                OperationResult transition = Transition(SessionPhase.Synchronizing);
                if (!transition.Succeeded)
                {
                    return SetLastResult(transition);
                }

                OperationResult sync = await RunWithTimeout(
                    token => _game.SynchronizePeerAsync(peer, token),
                    _config.InitialSyncTimeout,
                    cancellationToken,
                    MultiplayerErrorCode.SynchronizationFailed,
                    "multiplayer.error.peer_sync_failed");

                if (!IsCurrent(generation))
                {
                    return SetLastResult(StaleResult());
                }

                if (!sync.Succeeded)
                {
                    if (_room != null)
                    {
                        _room = _room.WithSession(
                            _room.SessionGeneration,
                            SessionPhase.InRoom,
                            false,
                            true);
                        PublishCurrentRoomState();
                    }

                    Transition(SessionPhase.InRoom);
                    return SetLastResult(sync);
                }

                if (_room != null)
                {
                    _room = _room.WithMemberPhase(peer.User.Id, MemberConnectionPhase.Ready);
                }

                SessionPhase target = MeetsReadyConditions() ? SessionPhase.Ready : SessionPhase.InRoom;
                if (_room != null)
                {
                    _room = _room.WithSession(
                        _room.SessionGeneration,
                        target,
                        false,
                        true);
                    OperationResult publish = PublishCurrentRoomState();
                    if (!publish.Succeeded)
                    {
                        _room = _room.WithSession(
                            _room.SessionGeneration,
                            SessionPhase.InRoom,
                            false,
                            true);
                        PublishCurrentRoomState();
                        Transition(SessionPhase.InRoom);
                        return SetLastResult(publish);
                    }
                }

                OperationResult targetTransition = Transition(target);
                if (!targetTransition.Succeeded)
                {
                    return SetLastResult(targetTransition);
                }

                return SetLastResult(OperationResult.Success());
            }
            finally
            {
                EndOperation();
            }
        }

        public OperationResult ConfirmLocalSynchronization(SessionId sessionId, long sessionGeneration)
        {
            if (_room == null || _room.SessionId != sessionId ||
                _room.SessionGeneration != sessionGeneration)
            {
                return SetLastResult(OperationResult.Failure(
                    MultiplayerErrorCode.SessionMismatch,
                    "multiplayer.error.sync_session_mismatch"));
            }

            if (Phase != SessionPhase.Synchronizing)
            {
                return SetLastResult(OperationResult.Failure(
                    MultiplayerErrorCode.InvalidState,
                    "multiplayer.error.sync_confirmation_unexpected"));
            }

            _room = _room.WithMemberPhase(_platform.LocalUser.Id, MemberConnectionPhase.Ready);
            _reconnectRemaining = TimeSpan.Zero;
            SessionPhase target = _room.HasStarted || _room.Phase == SessionPhase.InGame
                ? SessionPhase.InGame
                : SessionPhase.Ready;
            _room = _room.WithSession(
                _room.SessionGeneration,
                target,
                target == SessionPhase.InGame,
                _room.IsJoinable);
            OperationResult transition = Transition(target);
            return SetLastResult(transition);
        }

        public async Task<OperationResult> StartGameAsync(CancellationToken cancellationToken)
        {
            OperationResult busy;
            if (!TryBeginOperation(out busy))
            {
                return busy;
            }

            try
            {
                int operationGeneration = _operationGeneration.Begin();
                if (!MeetsReadyConditions())
                {
                    return SetLastResult(OperationResult.Failure(
                        MultiplayerErrorCode.InvalidState,
                        "multiplayer.error.start_conditions_not_met"));
                }

                OperationResult transition = Transition(SessionPhase.Starting);
                if (!transition.Succeeded)
                {
                    return SetLastResult(transition);
                }

                long startingGeneration = _room.SessionGeneration + 1;
                _room = _room.WithSession(startingGeneration, SessionPhase.Starting, false, false);
                NotifyChanged();

                OperationResult publish = PublishCurrentRoomState();
                if (!publish.Succeeded)
                {
                    _room = _room.WithSession(
                        startingGeneration + 1,
                        SessionPhase.Ready,
                        false,
                        true);
                    OperationResult rollbackPublish = PublishCurrentRoomState();
                    if (!rollbackPublish.Succeeded)
                    {
                        _room = _room.WithSession(
                            startingGeneration + 1,
                            SessionPhase.Failed,
                            false,
                            false);
                        Transition(SessionPhase.Failed);
                        return SetLastResult(rollbackPublish);
                    }

                    Transition(SessionPhase.Ready);
                    return SetLastResult(publish);
                }

                OperationResult prepare = await RunWithTimeout(
                    token => _game.PrepareSessionAsync(
                        new MultiplayerSessionContext(_room, _platform.LocalUser, true),
                        token),
                    _config.StartTimeout,
                    cancellationToken,
                    MultiplayerErrorCode.SynchronizationFailed,
                    "multiplayer.error.start_failed");

                if (!IsCurrent(operationGeneration))
                {
                    return SetLastResult(StaleResult());
                }

                if (!prepare.Succeeded)
                {
                    _room = _room.WithSession(
                        startingGeneration + 1,
                        SessionPhase.Ready,
                        false,
                        true);
                    OperationResult rollbackPublish = PublishCurrentRoomState();
                    if (!rollbackPublish.Succeeded)
                    {
                        _room = _room.WithSession(
                            startingGeneration + 1,
                            SessionPhase.Failed,
                            false,
                            false);
                        Transition(SessionPhase.Failed);
                        return SetLastResult(rollbackPublish);
                    }

                    Transition(SessionPhase.Ready);
                    return SetLastResult(prepare);
                }

                _room = _room.WithSession(
                    startingGeneration,
                    SessionPhase.InGame,
                    true,
                    _config.AllowJoinInProgress);
                publish = PublishCurrentRoomState();
                if (!publish.Succeeded)
                {
                    _room = _room.WithSession(
                        startingGeneration,
                        SessionPhase.Failed,
                        true,
                        false);
                    PublishCurrentRoomState();
                    Transition(SessionPhase.Failed);
                    return SetLastResult(publish);
                }

                Transition(SessionPhase.InGame);
                return SetLastResult(OperationResult.Success());
            }
            finally
            {
                EndOperation();
            }
        }

        public async Task<OperationResult> RestorePeerAsync(
            MultiplayerPeer peer,
            CancellationToken cancellationToken)
        {
            OperationResult busy;
            if (!TryBeginOperation(out busy))
            {
                return busy;
            }

            try
            {
                int generation = _operationGeneration.Begin();
                if (!_driver.IsServer || Phase != SessionPhase.InGame)
                {
                    return SetLastResult(OperationResult.Failure(
                        MultiplayerErrorCode.InvalidState,
                        "multiplayer.error.server_restore_not_available"));
                }

                OperationResult restore = await RunWithTimeout(
                    token => _game.RestorePeerAsync(peer, token),
                    _config.InitialSyncTimeout,
                    cancellationToken,
                    MultiplayerErrorCode.SynchronizationFailed,
                    "multiplayer.error.restore_failed");

                if (!IsCurrent(generation))
                {
                    return SetLastResult(StaleResult());
                }

                if (!restore.Succeeded)
                {
                    return SetLastResult(restore);
                }

                if (_room != null)
                {
                    _room = _room.WithMemberPhase(peer.User.Id, MemberConnectionPhase.Ready);
                }

                return SetLastResult(OperationResult.Success());
            }
            finally
            {
                EndOperation();
            }
        }

        public Task<OperationResult<RoomSnapshot>> RetryAsync(CancellationToken cancellationToken)
        {
            switch (_retryOperation)
            {
                case RetryOperationKind.CreateRoom:
                    return CreateRoomAsync(cancellationToken);
                case RetryOperationKind.JoinRoom:
                    return JoinRoomAsync(_retryRoomId, cancellationToken);
                default:
                    return Task.FromResult(OperationResult<RoomSnapshot>.Failure(
                        MultiplayerErrorCode.InvalidState,
                        "multiplayer.error.retry_not_available"));
            }
        }

        public OperationResult CancelReconnect()
        {
            if (Phase != SessionPhase.Reconnecting)
            {
                return SetLastResult(OperationResult.Failure(
                    MultiplayerErrorCode.InvalidState,
                    "multiplayer.error.reconnect_not_active"));
            }

            _operationGeneration.Invalidate();
            _reconnectRemaining = TimeSpan.Zero;
            Transition(SessionPhase.Recovery);
            return SetLastResult(OperationResult.Failure(
                MultiplayerErrorCode.Cancelled,
                "multiplayer.error.reconnect_cancelled"));
        }

        public OperationResult ExpireReconnect()
        {
            if (Phase != SessionPhase.Reconnecting)
            {
                return SetLastResult(OperationResult.Failure(
                    MultiplayerErrorCode.InvalidState,
                    "multiplayer.error.reconnect_not_active"));
            }

            _operationGeneration.Invalidate();
            _reconnectRemaining = TimeSpan.Zero;
            Transition(SessionPhase.Recovery);
            return SetLastResult(OperationResult.Failure(
                MultiplayerErrorCode.ReconnectExpired,
                "multiplayer.error.reconnect_expired"));
        }

        public OperationResult ExpirePeerReconnect(MultiplayerPeer peer)
        {
            if (!_driver.IsServer || Phase != SessionPhase.InGame)
            {
                return SetLastResult(OperationResult.Failure(
                    MultiplayerErrorCode.InvalidState,
                    "multiplayer.error.server_peer_expiry_not_available"));
            }

            _game.OnPeerReconnectExpired(peer);
            if (_room != null)
            {
                _room = _room.WithMemberPhase(peer.User.Id, MemberConnectionPhase.Disconnected);
            }

            return SetLastResult(OperationResult.Success());
        }

        public void UpdateReconnectElapsed(TimeSpan elapsed)
        {
            if (Phase != SessionPhase.Reconnecting)
            {
                return;
            }

            _reconnectRemaining = elapsed >= _config.ReconnectHardDeadline
                ? TimeSpan.Zero
                : _config.ReconnectHardDeadline - elapsed;
            NotifyChanged();
        }

        public async Task<OperationResult> LeaveAsync(CancellationToken cancellationToken)
        {
            if (Phase == SessionPhase.Idle)
            {
                return OperationResult.Success();
            }

            OperationResult busy;
            if (!TryBeginOperation(out busy))
            {
                return busy;
            }

            try
            {
                _operationGeneration.Invalidate();
                _intentionalLeave = true;

                OperationResult transition = Transition(SessionPhase.Leaving);
                if (!transition.Succeeded)
                {
                    return SetLastResult(transition);
                }

                OperationResult stop = await RunWithTimeout(
                    token => _driver.StopAsync(DisconnectIntent.IntentionalLeave, token),
                    _config.LeaveTimeout,
                    cancellationToken,
                    MultiplayerErrorCode.LeaveFailed,
                    "multiplayer.error.stop_failed");

                OperationResult leave = await RunWithTimeout(
                    token => _platform.LeaveAsync(token),
                    _config.LeaveTimeout,
                    cancellationToken,
                    MultiplayerErrorCode.LeaveFailed,
                    "multiplayer.error.leave_failed");

                _game.OnSessionEnded(SessionEndReason.IntentionalLeave);
                _room = null;
                _retryOperation = RetryOperationKind.None;
                _retryRoomId = default(RoomId);
                _inviteRouter.Clear();
                _reconnectRemaining = TimeSpan.Zero;
                Transition(SessionPhase.Idle);

                OperationResult result = !stop.Succeeded ? stop : leave;
                return SetLastResult(result);
            }
            finally
            {
                EndOperation();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stateMachine.PhaseChanged -= OnPhaseChanged;
            _platform.RoomEvent -= OnRoomEvent;
            _platform.JoinRequested -= OnJoinRequested;
            _driver.ConnectionEvent -= OnConnectionEvent;
            await _platform.DisposeAsync();
            _operationLock.Dispose();
        }

        private bool TryBeginOperation(out OperationResult failure)
        {
            ThrowIfDisposed();
            if (!_operationLock.Wait(0))
            {
                failure = OperationResult.Failure(
                    MultiplayerErrorCode.Busy,
                    "multiplayer.error.operation_in_progress");
                return false;
            }

            _operationInProgress = true;
            failure = OperationResult.Success();
            NotifyChanged();
            return true;
        }

        private OperationResult InitializeCore(CancellationToken cancellationToken)
        {
            try
            {
                _operationGeneration.Begin();
                OperationResult transition = Transition(SessionPhase.Initializing);
                if (!transition.Succeeded)
                {
                    return SetLastResult(transition);
                }

                IReadOnlyList<string> configErrors = _config.ValidateConfiguration();
                if (configErrors.Count > 0)
                {
                    OperationResult failure = OperationResult.Failure(
                        MultiplayerErrorCode.InvalidConfiguration,
                        configErrors[0]);
                    Transition(SessionPhase.Failed);
                    return SetLastResult(failure);
                }

                if (!_platform.IsAvailable)
                {
                    OperationResult failure = OperationResult.Failure(
                        MultiplayerErrorCode.PlatformUnavailable,
                        string.IsNullOrWhiteSpace(_platform.UnavailableReasonKey)
                            ? "multiplayer.error.platform_unavailable"
                            : _platform.UnavailableReasonKey);
                    Transition(SessionPhase.Failed);
                    return SetLastResult(failure);
                }

                cancellationToken.ThrowIfCancellationRequested();
                Transition(SessionPhase.Idle);
                return SetLastResult(OperationResult.Success());
            }
            catch (OperationCanceledException)
            {
                OperationResult failure = OperationResult.Failure(
                    MultiplayerErrorCode.Cancelled,
                    "multiplayer.error.cancelled");
                TransitionToFailedIfAllowed();
                return SetLastResult(failure);
            }
        }

        private void EndOperation()
        {
            _operationInProgress = false;
            _operationLock.Release();
            NotifyChanged();
        }

        private OperationResult Transition(SessionPhase target)
        {
            return _stateMachine.TryTransition(target);
        }

        private OperationResult PublishCurrentRoomState()
        {
            if (_room == null || !_room.IsHost(_platform.LocalUser.Id))
            {
                return OperationResult.Success();
            }

            IRoomStatePublisher publisher = _platform as IRoomStatePublisher;
            return publisher == null
                ? OperationResult.Success()
                : publisher.PublishRoomState(_room);
        }

        private void TransitionToFailedIfAllowed()
        {
            if (MultiplayerSessionStateMachine.CanTransition(Phase, SessionPhase.Failed))
            {
                Transition(SessionPhase.Failed);
            }
        }

        private bool IsCurrent(int generation)
        {
            return _operationGeneration.IsCurrent(generation);
        }

        private OperationResult<RoomSnapshot> FailRoomOperation(OperationResult failure)
        {
            TransitionToFailedIfAllowed();
            _room = null;
            SetLastResult(failure);
            return OperationResult<RoomSnapshot>.FromFailure(failure);
        }

        private OperationResult<RoomSnapshot> StaleRoomResult()
        {
            return OperationResult<RoomSnapshot>.FromFailure(SetLastResult(StaleResult()));
        }

        private static OperationResult StaleResult()
        {
            return OperationResult.Failure(
                MultiplayerErrorCode.StaleOperation,
                "multiplayer.error.stale_operation");
        }

        private OperationResult SetLastResult(OperationResult result)
        {
            _lastResult = result;
            NotifyChanged();
            return result;
        }

        private bool MeetsReadyConditions()
        {
            if (_room == null || !_driver.IsServer || !_room.IsHost(_platform.LocalUser.Id) ||
                _room.Members.Count < _config.MinimumPlayersToStart)
            {
                return false;
            }

            for (int i = 0; i < _room.Members.Count; i++)
            {
                if (_room.Members[i].ConnectionPhase != MemberConnectionPhase.Ready)
                {
                    return false;
                }
            }

            return true;
        }

        private async Task CleanupFailedJoinAsync()
        {
            await RunWithTimeout(
                token => _driver.StopAsync(DisconnectIntent.Failure, token),
                _config.LeaveTimeout,
                CancellationToken.None,
                MultiplayerErrorCode.LeaveFailed,
                "multiplayer.error.cleanup_stop_failed");

            await RunWithTimeout(
                token => _platform.LeaveAsync(token),
                _config.LeaveTimeout,
                CancellationToken.None,
                MultiplayerErrorCode.LeaveFailed,
                "multiplayer.error.cleanup_leave_failed");
        }

        private void OnPhaseChanged(SessionPhase previous, SessionPhase current)
        {
            NotifyChanged();
        }

        private void OnJoinRequested(JoinRequest request)
        {
            InviteRouteAction action = _inviteRouter.Route(request, Phase, _room);
            Action<JoinRequest, InviteRouteAction> handler = JoinRequestRouted;
            if (handler != null)
            {
                handler(request, action);
            }

            NotifyChanged();
        }

        private void OnRoomEvent(PlatformRoomEvent roomEvent)
        {
            if (_room == null || roomEvent.RoomId != _room.Id ||
                roomEvent.SessionGeneration != _room.SessionGeneration)
            {
                return;
            }

            if (roomEvent.Snapshot != null)
            {
                _room = MergeConnectionPhases(_room, roomEvent.Snapshot);
            }

            if (roomEvent.Type == RoomEventType.MemberLeft)
            {
                if (Phase == SessionPhase.Ready || Phase == SessionPhase.Synchronizing)
                {
                    _room = _room.WithSession(
                        _room.SessionGeneration,
                        SessionPhase.InRoom,
                        false,
                        true);
                    Transition(SessionPhase.InRoom);
                    OperationResult publish = PublishCurrentRoomState();
                    if (!publish.Succeeded)
                    {
                        SetLastResult(publish);
                    }
                }
            }
            else if (roomEvent.Type == RoomEventType.HostLeft)
            {
                EnterHostLeftRecovery();
            }

            NotifyChanged();
        }

        private static RoomSnapshot MergeConnectionPhases(
            RoomSnapshot current,
            RoomSnapshot incoming)
        {
            if (current == null || incoming == null)
            {
                return incoming;
            }

            List<RoomMember> members = new List<RoomMember>(incoming.Members.Count);
            for (int i = 0; i < incoming.Members.Count; i++)
            {
                RoomMember next = incoming.Members[i];
                for (int j = 0; j < current.Members.Count; j++)
                {
                    RoomMember previous = current.Members[j];
                    if (previous.User.Id == next.User.Id)
                    {
                        next = next.WithConnectionPhase(
                            SelectConnectionPhase(previous.ConnectionPhase, next.ConnectionPhase));
                        break;
                    }
                }

                members.Add(next);
            }

            return new RoomSnapshot(
                incoming.Id,
                incoming.HostId,
                members,
                incoming.MaxMembers,
                incoming.Visibility,
                incoming.IsJoinable,
                incoming.ProductId,
                incoming.ProtocolVersion,
                incoming.GameProtocolVersion,
                incoming.ContentRevision,
                incoming.BuildVersion,
                incoming.GameRoomId,
                incoming.SessionId,
                incoming.SessionGeneration,
                incoming.Phase,
                incoming.HasStarted,
                incoming.ConnectionAddress);
        }

        private static MemberConnectionPhase SelectConnectionPhase(
            MemberConnectionPhase current,
            MemberConnectionPhase incoming)
        {
            if (incoming == MemberConnectionPhase.Disconnected)
            {
                return incoming;
            }

            if (current == MemberConnectionPhase.Disconnected)
            {
                return incoming == MemberConnectionPhase.LobbyOnly ? current : incoming;
            }

            return ConnectionPhaseRank(current) >= ConnectionPhaseRank(incoming)
                ? current
                : incoming;
        }

        private static int ConnectionPhaseRank(MemberConnectionPhase phase)
        {
            switch (phase)
            {
                case MemberConnectionPhase.Connecting:
                    return 1;
                case MemberConnectionPhase.Connected:
                    return 2;
                case MemberConnectionPhase.Synchronizing:
                    return 3;
                case MemberConnectionPhase.Ready:
                    return 4;
                default:
                    return 0;
            }
        }

        private void OnConnectionEvent(ConnectionEvent connectionEvent)
        {
            if (_intentionalLeave)
            {
                return;
            }

            if (connectionEvent.ErrorCode == MultiplayerErrorCode.HostLeft)
            {
                EnterHostLeftRecovery();
                return;
            }

            if (connectionEvent.Type == ConnectionEventType.LocalDisconnected &&
                Phase == SessionPhase.InGame && _config.ReconnectEnabled)
            {
                _reconnectRemaining = _config.ReconnectHardDeadline;
                Transition(SessionPhase.Reconnecting);
                SetLastResult(OperationResult.Failure(
                    MultiplayerErrorCode.TransportFailed,
                    string.IsNullOrWhiteSpace(connectionEvent.MessageKey)
                        ? "multiplayer.error.connection_lost"
                        : connectionEvent.MessageKey));
            }
            else if (connectionEvent.Type == ConnectionEventType.ClientConnected &&
                     Phase == SessionPhase.Reconnecting)
            {
                Transition(SessionPhase.Synchronizing);
            }
            else if (connectionEvent.Type == ConnectionEventType.PeerDisconnected &&
                     Phase == SessionPhase.InGame && _driver.IsServer)
            {
                MultiplayerPeer peer;
                if (TryGetPeer(connectionEvent.UserId, out peer))
                {
                    _room = _room.WithMemberPhase(peer.User.Id, MemberConnectionPhase.Disconnected);
                    OperationResult publish = PublishCurrentRoomState();
                    if (!publish.Succeeded)
                    {
                        SetLastResult(publish);
                    }

                    _game.OnPeerDisconnected(peer, _config.ReconnectGracePeriod);
                    NotifyChanged();
                }
            }
        }

        private bool TryGetPeer(PlatformUserId userId, out MultiplayerPeer peer)
        {
            if (_room != null)
            {
                for (int i = 0; i < _room.Members.Count; i++)
                {
                    RoomMember member = _room.Members[i];
                    if (member.User.Id == userId)
                    {
                        peer = new MultiplayerPeer(member.User, member.SeatIndex);
                        return true;
                    }
                }
            }

            peer = default(MultiplayerPeer);
            return false;
        }

        private void EnterHostLeftRecovery()
        {
            if (Phase == SessionPhase.InGame)
            {
                Transition(SessionPhase.Reconnecting);
            }

            if (Phase == SessionPhase.Reconnecting)
            {
                _reconnectRemaining = TimeSpan.Zero;
                Transition(SessionPhase.Recovery);
                SetLastResult(OperationResult.Failure(
                    MultiplayerErrorCode.HostLeft,
                    "multiplayer.error.host_left"));
            }
        }

        private void NotifyChanged()
        {
            Action handler = Changed;
            if (handler != null)
            {
                handler();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MultiplayerSessionCoordinator));
            }
        }

        private static async Task<OperationResult<T>> RunWithTimeout<T>(
            Func<CancellationToken, Task<OperationResult<T>>> operation,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            MultiplayerErrorCode exceptionCode,
            string exceptionMessageKey)
        {
            using (CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutSource.CancelAfter(timeout);
                try
                {
                    return await operation(timeoutSource.Token);
                }
                catch (OperationCanceledException)
                {
                    return cancellationToken.IsCancellationRequested
                        ? OperationResult<T>.Failure(MultiplayerErrorCode.Cancelled, "multiplayer.error.cancelled")
                        : OperationResult<T>.Failure(MultiplayerErrorCode.Timeout, "multiplayer.error.timeout");
                }
                catch (Exception exception)
                {
                    return OperationResult<T>.Failure(
                        exceptionCode,
                        exceptionMessageKey,
                        exception.GetType().Name);
                }
            }
        }

        private static async Task<OperationResult> RunWithTimeout(
            Func<CancellationToken, Task<OperationResult>> operation,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            MultiplayerErrorCode exceptionCode,
            string exceptionMessageKey)
        {
            using (CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutSource.CancelAfter(timeout);
                try
                {
                    return await operation(timeoutSource.Token);
                }
                catch (OperationCanceledException)
                {
                    return cancellationToken.IsCancellationRequested
                        ? OperationResult.Failure(MultiplayerErrorCode.Cancelled, "multiplayer.error.cancelled")
                        : OperationResult.Failure(MultiplayerErrorCode.Timeout, "multiplayer.error.timeout");
                }
                catch (Exception exception)
                {
                    return OperationResult.Failure(
                        exceptionCode,
                        exceptionMessageKey,
                        exception.GetType().Name);
                }
            }
        }
    }
}
