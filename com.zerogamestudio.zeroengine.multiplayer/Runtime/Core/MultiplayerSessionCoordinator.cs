using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.Multiplayer
{
    public sealed class MultiplayerSessionCoordinator : IAsyncDisposable
    {
        private readonly IMultiplayerSessionSettings _config;
        private readonly IPlatformRoomService _platform;
        private readonly INetworkConnectionDriver _driver;
        private readonly IMultiplayerGameAdapter _game;
        private readonly MultiplayerSessionStateMachine _stateMachine;
        private readonly OperationGenerationGate _operationGeneration = new OperationGenerationGate();
        private readonly InviteRouter _inviteRouter = new InviteRouter();
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private readonly Dictionary<PlatformUserId, CancellationTokenSource> _peerReconnectExpiry =
            new Dictionary<PlatformUserId, CancellationTokenSource>();

        private RoomSnapshot _room;
        private OperationResult _lastResult = OperationResult.Success();
        private RetryOperationKind _retryOperation;
        private RoomId _retryRoomId;
        private bool _operationInProgress;
        private bool _intentionalLeave;
        private bool _cleanupIncomplete;
        private bool _disposed;
        private TimeSpan _reconnectRemaining;
        private int _reconnectAttempt;
        private CancellationTokenSource _activeReconnectCancellation;

        public MultiplayerSessionCoordinator(
            IMultiplayerSessionSettings config,
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
                _reconnectRemaining,
                _reconnectAttempt);
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
            if (_cleanupIncomplete)
            {
                return OperationResult<RoomSnapshot>.Failure(
                    MultiplayerErrorCode.InvalidState,
                    "multiplayer.error.cleanup_required");
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
                    return await FailAfterCleanupAsync(OperationResult.Failure(
                        MultiplayerErrorCode.CreateFailed,
                        "multiplayer.error.create_returned_no_room"));
                }

                _room = create.Value;
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
                    return await FailAfterCleanupAsync(prepare);
                }

                transition = Transition(SessionPhase.Connecting);
                if (!transition.Succeeded)
                {
                    return await FailAfterCleanupAsync(transition);
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
                    return await FailAfterCleanupAsync(start);
                }

                transition = Transition(SessionPhase.Synchronizing);
                if (!transition.Succeeded)
                {
                    return await FailAfterCleanupAsync(transition);
                }

                OperationResult synchronize = await RunWithTimeout(
                    token => _game.SynchronizeLocalAsync(
                        new MultiplayerSessionContext(_room, _platform.LocalUser, true),
                        token),
                    _config.InitialSyncTimeout,
                    cancellationToken,
                    MultiplayerErrorCode.SynchronizationFailed,
                    "multiplayer.error.host_sync_failed");
                if (!IsCurrent(generation))
                {
                    return StaleRoomResult();
                }

                if (!synchronize.Succeeded)
                {
                    return await FailAfterCleanupAsync(synchronize);
                }

                _room = _room
                    .WithMemberPhase(_platform.LocalUser.Id, MemberConnectionPhase.Ready)
                    .WithSession(_room.SessionGeneration, SessionPhase.InRoom, false, true);
                OperationResult publish = PublishCurrentRoomState();
                if (!publish.Succeeded)
                {
                    return await FailAfterCleanupAsync(publish);
                }

                transition = Transition(SessionPhase.InRoom);
                if (!transition.Succeeded)
                {
                    return await FailAfterCleanupAsync(transition);
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
            if (_cleanupIncomplete)
            {
                return OperationResult<RoomSnapshot>.Failure(
                    MultiplayerErrorCode.InvalidState,
                    "multiplayer.error.cleanup_required");
            }

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
                    return await FailAfterCleanupAsync(OperationResult.Failure(
                        MultiplayerErrorCode.JoinFailed,
                        "multiplayer.error.join_returned_no_room"));
                }

                _room = join.Value;
                if (!_room.IsJoinable)
                {
                    return await FailAfterCleanupAsync(OperationResult.Failure(
                        _room.HasStarted || _room.Phase == SessionPhase.Starting ||
                        _room.Phase == SessionPhase.InGame
                            ? MultiplayerErrorCode.RoomStarted
                            : MultiplayerErrorCode.JoinFailed,
                        "multiplayer.error.room_not_joinable"));
                }

                CompatibilityDescriptor localCompatibility;
                try
                {
                    localCompatibility = _game.GetCompatibility(_room.GameRoomId);
                }
                catch (Exception exception)
                {
                    return await FailAfterCleanupAsync(OperationResult.Failure(
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
                    return await FailAfterCleanupAsync(compatibility);
                }

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
                    return await FailAfterCleanupAsync(prepare);
                }

                transition = Transition(SessionPhase.Connecting);
                if (!transition.Succeeded)
                {
                    return await FailAfterCleanupAsync(transition);
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
                    return await FailAfterCleanupAsync(start);
                }

                transition = Transition(SessionPhase.Synchronizing);
                if (!transition.Succeeded)
                {
                    return await FailAfterCleanupAsync(transition);
                }

                OperationResult synchronize = await RunWithTimeout(
                    token => _game.SynchronizeLocalAsync(
                        new MultiplayerSessionContext(_room, _platform.LocalUser, false),
                        token),
                    _config.InitialSyncTimeout,
                    cancellationToken,
                    MultiplayerErrorCode.SynchronizationFailed,
                    "multiplayer.error.client_sync_failed");

                if (!IsCurrent(generation))
                {
                    return StaleRoomResult();
                }

                if (!synchronize.Succeeded)
                {
                    return await FailAfterCleanupAsync(synchronize);
                }

                _room = _room.WithMemberPhase(_platform.LocalUser.Id, MemberConnectionPhase.Ready);
                SessionPhase target = _room.HasStarted || _room.Phase == SessionPhase.InGame
                    ? SessionPhase.InGame
                    : SessionPhase.Ready;
                _room = _room.WithSession(
                    _room.SessionGeneration,
                    target,
                    target == SessionPhase.InGame,
                    _room.IsJoinable);
                transition = Transition(target);
                if (!transition.Succeeded)
                {
                    return await FailAfterCleanupAsync(transition);
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

        public OperationResult DismissJoinRequest()
        {
            if (!_inviteRouter.HasPending)
            {
                return SetLastResult(OperationResult.Failure(
                    MultiplayerErrorCode.InvalidState,
                    "multiplayer.error.no_pending_invite"));
            }

            _inviteRouter.Clear();
            NotifyChanged();
            return SetLastResult(OperationResult.Success());
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

        public OperationResult ConfirmRemoteSessionStarted(
            SessionId sessionId,
            long sessionGeneration)
        {
            if (_driver.IsServer || _room == null || _room.IsHost(_platform.LocalUser.Id))
            {
                return SetLastResult(OperationResult.Failure(
                    MultiplayerErrorCode.InvalidState,
                    "multiplayer.error.remote_start_confirmation_unavailable"));
            }

            bool validGeneration =
                (Phase == SessionPhase.Ready &&
                 sessionGeneration == _room.SessionGeneration + 1) ||
                (Phase == SessionPhase.Starting &&
                 sessionGeneration == _room.SessionGeneration) ||
                (Phase == SessionPhase.InGame &&
                 sessionGeneration == _room.SessionGeneration);
            if (_room.SessionId != sessionId || !validGeneration)
            {
                return SetLastResult(OperationResult.Failure(
                    MultiplayerErrorCode.SessionMismatch,
                    "multiplayer.error.remote_start_session_mismatch"));
            }

            if (Phase == SessionPhase.InGame)
            {
                return SetLastResult(OperationResult.Success());
            }

            if (Phase != SessionPhase.Ready && Phase != SessionPhase.Starting)
            {
                return SetLastResult(OperationResult.Failure(
                    MultiplayerErrorCode.InvalidState,
                    "multiplayer.error.remote_start_confirmation_unexpected"));
            }

            _room = _room.WithSession(
                sessionGeneration,
                SessionPhase.InGame,
                true,
                _config.AllowJoinInProgress);
            OperationResult transition = Transition(SessionPhase.InGame);
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
                    OperationResult publish = PublishCurrentRoomState();
                    if (!publish.Succeeded)
                    {
                        return SetLastResult(publish);
                    }
                }

                return SetLastResult(OperationResult.Success());
            }
            finally
            {
                EndOperation();
            }
        }

        public async Task<OperationResult> ReconnectClientAsync(CancellationToken cancellationToken)
        {
            if (Phase != SessionPhase.Reconnecting || _driver.IsServer || _room == null)
            {
                return SetLastResult(OperationResult.Failure(
                    MultiplayerErrorCode.InvalidState,
                    "multiplayer.error.client_reconnect_not_available"));
            }

            OperationResult busy;
            if (!TryBeginOperation(out busy))
            {
                return busy;
            }

            CancellationTokenSource reconnectCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeReconnectCancellation = reconnectCancellation;
            try
            {
                int generation = _operationGeneration.Begin();
                RoomSnapshot reconnectRoom = _room;
                ReconnectPolicy policy = MultiplayerSessionSettings.CreateReconnectPolicy(_config);
                Stopwatch stopwatch = Stopwatch.StartNew();
                OperationResult lastFailure = OperationResult.Failure(
                    MultiplayerErrorCode.TransportFailed,
                    "multiplayer.error.connection_lost");

                for (int attemptIndex = 0; attemptIndex < policy.MaxAttempts; attemptIndex++)
                {
                    if (!IsCurrent(generation) || Phase != SessionPhase.Reconnecting)
                    {
                        return SetLastResult(OperationResult.Failure(
                            MultiplayerErrorCode.Cancelled,
                            "multiplayer.error.reconnect_cancelled"));
                    }

                    ReconnectBlockReason block = policy.Evaluate(
                        attemptIndex,
                        stopwatch.Elapsed,
                        reconnectCancellation.IsCancellationRequested,
                        out ReconnectAttempt attempt);
                    if (block != ReconnectBlockReason.None)
                    {
                        break;
                    }

                    _reconnectAttempt = attempt.Number;
                    UpdateReconnectElapsed(stopwatch.Elapsed);
                    try
                    {
                        if (attempt.Delay > TimeSpan.Zero)
                        {
                            await Task.Delay(attempt.Delay, reconnectCancellation.Token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return SetLastResult(OperationResult.Failure(
                            MultiplayerErrorCode.Cancelled,
                            "multiplayer.error.reconnect_cancelled"));
                    }

                    UpdateReconnectElapsed(stopwatch.Elapsed);
                    OperationResult<RoomSnapshot> refresh = await RunWithTimeout(
                        token => _platform.RefreshAsync(token),
                        attempt.Timeout,
                        reconnectCancellation.Token,
                        MultiplayerErrorCode.RoomNotFound,
                        "multiplayer.error.reconnect_refresh_failed");
                    if (!refresh.Succeeded)
                    {
                        lastFailure = refresh.Result;
                        if (refresh.ErrorCode == MultiplayerErrorCode.HostLeft)
                        {
                            EnterHostLeftRecovery();
                            return SetLastResult(refresh.Result);
                        }

                        continue;
                    }

                    RoomSnapshot refreshedRoom = refresh.Value;
                    if (refreshedRoom == null || refreshedRoom.HostId != reconnectRoom.HostId ||
                        refreshedRoom.SessionId != reconnectRoom.SessionId ||
                        refreshedRoom.SessionGeneration > reconnectRoom.SessionGeneration)
                    {
                        EnterHostLeftRecovery();
                        return SetLastResult(OperationResult.Failure(
                            MultiplayerErrorCode.HostLeft,
                            "multiplayer.error.reconnect_session_replaced"));
                    }

                    if (refreshedRoom.SessionGeneration == reconnectRoom.SessionGeneration)
                    {
                        _room = MergeConnectionPhases(reconnectRoom, refreshedRoom).WithSession(
                            reconnectRoom.SessionGeneration,
                            reconnectRoom.Phase,
                            reconnectRoom.HasStarted,
                            reconnectRoom.IsJoinable);
                    }
                    else
                    {
                        _room = reconnectRoom;
                    }

                    MultiplayerSessionContext context = new MultiplayerSessionContext(
                        _room,
                        _platform.LocalUser,
                        false);
                    OperationResult prepare = await RunWithTimeout(
                        token => _game.PrepareSessionAsync(context, token),
                        attempt.Timeout,
                        reconnectCancellation.Token,
                        MultiplayerErrorCode.SynchronizationFailed,
                        "multiplayer.error.reconnect_prepare_failed");
                    if (!prepare.Succeeded)
                    {
                        lastFailure = prepare;
                        continue;
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
                        attempt.Timeout,
                        reconnectCancellation.Token,
                        MultiplayerErrorCode.TransportFailed,
                        "multiplayer.error.reconnect_transport_failed");
                    if (!start.Succeeded)
                    {
                        lastFailure = start;
                        continue;
                    }

                    if (Phase == SessionPhase.Reconnecting)
                    {
                        Transition(SessionPhase.Synchronizing);
                    }

                    OperationResult synchronize = await RunWithTimeout(
                        token => _game.SynchronizeLocalAsync(context, token),
                        Minimum(attempt.Timeout, _config.InitialSyncTimeout),
                        reconnectCancellation.Token,
                        MultiplayerErrorCode.SynchronizationFailed,
                        "multiplayer.error.reconnect_sync_failed");
                    if (synchronize.Succeeded)
                    {
                        _reconnectAttempt = 0;
                        _reconnectRemaining = TimeSpan.Zero;
                        return ConfirmLocalSynchronization(_room.SessionId, _room.SessionGeneration);
                    }

                    lastFailure = synchronize;
                    await RunWithTimeout(
                        token => _driver.StopAsync(DisconnectIntent.Failure, token),
                        _config.LeaveTimeout,
                        CancellationToken.None,
                        MultiplayerErrorCode.LeaveFailed,
                        "multiplayer.error.reconnect_cleanup_failed");
                    if (Phase == SessionPhase.Synchronizing)
                    {
                        Transition(SessionPhase.Reconnecting);
                    }
                }

                _reconnectAttempt = policy.MaxAttempts;
                if (Phase == SessionPhase.Reconnecting)
                {
                    return ExpireReconnect();
                }

                return SetLastResult(lastFailure);
            }
            finally
            {
                if (ReferenceEquals(_activeReconnectCancellation, reconnectCancellation))
                {
                    _activeReconnectCancellation = null;
                }

                reconnectCancellation.Dispose();
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

            _activeReconnectCancellation?.Cancel();
            _operationGeneration.Invalidate();
            _reconnectRemaining = TimeSpan.Zero;
            _reconnectAttempt = 0;
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
            _reconnectAttempt = 0;
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

                OperationResult result = !stop.Succeeded ? stop : leave;
                if (!result.Succeeded)
                {
                    _cleanupIncomplete = true;
                    Transition(SessionPhase.Failed);
                    return SetLastResult(result);
                }

                _cleanupIncomplete = false;
                _game.OnSessionEnded(SessionEndReason.IntentionalLeave);
                _room = null;
                _retryOperation = RetryOperationKind.None;
                _retryRoomId = default(RoomId);
                _inviteRouter.Clear();
                _reconnectRemaining = TimeSpan.Zero;
                _reconnectAttempt = 0;
                Transition(SessionPhase.Idle);
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
            _activeReconnectCancellation?.Cancel();
            _activeReconnectCancellation = null;
            var expiries = new List<CancellationTokenSource>(_peerReconnectExpiry.Values);
            _peerReconnectExpiry.Clear();
            foreach (CancellationTokenSource expiry in expiries)
            {
                expiry.Cancel();
            }
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

                IReadOnlyList<string> configErrors = MultiplayerSessionSettings.Validate(_config);
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

        private static TimeSpan Minimum(TimeSpan left, TimeSpan right)
        {
            return left <= right ? left : right;
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

        private async Task<OperationResult> CleanupFailedJoinAsync()
        {
            OperationResult stop = await RunWithTimeout(
                token => _driver.StopAsync(DisconnectIntent.Failure, token),
                _config.LeaveTimeout,
                CancellationToken.None,
                MultiplayerErrorCode.LeaveFailed,
                "multiplayer.error.cleanup_stop_failed");

            OperationResult leave = await RunWithTimeout(
                token => _platform.LeaveAsync(token),
                _config.LeaveTimeout,
                CancellationToken.None,
                MultiplayerErrorCode.LeaveFailed,
                "multiplayer.error.cleanup_leave_failed");
            return !stop.Succeeded ? stop : leave;
        }

        private async Task<OperationResult<RoomSnapshot>> FailAfterCleanupAsync(
            OperationResult failure)
        {
            OperationResult cleanup = await CleanupFailedJoinAsync();
            _cleanupIncomplete = !cleanup.Succeeded;
            if (_cleanupIncomplete)
            {
                _retryOperation = RetryOperationKind.None;
                return FailRoomOperation(cleanup);
            }

            return FailRoomOperation(failure);
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
            if (_room == null || roomEvent.RoomId != _room.Id)
            {
                return;
            }

            if (roomEvent.SessionGeneration != _room.SessionGeneration &&
                !IsValidRemoteGenerationAdvance(roomEvent.Snapshot))
            {
                return;
            }

            if (roomEvent.Type == RoomEventType.HostLeft)
            {
                EnterHostLeftRecovery();
                NotifyChanged();
                return;
            }

            if (roomEvent.Type == RoomEventType.MemberLeft && Phase == SessionPhase.InGame)
            {
                MultiplayerPeer disconnectedPeer;
                if (TryGetPeer(roomEvent.MemberId, out disconnectedPeer))
                {
                    MarkPeerDisconnected(disconnectedPeer);
                }

                return;
            }

            if (roomEvent.Snapshot != null)
            {
                _room = MergeConnectionPhases(_room, roomEvent.Snapshot);
                ApplyRemoteRoomPhase();
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
            NotifyChanged();
        }

        private void ApplyRemoteRoomPhase()
        {
            if (_room == null || _room.IsHost(_platform.LocalUser.Id))
            {
                return;
            }

            if (_room.Phase == SessionPhase.Starting && Phase == SessionPhase.Ready)
            {
                Transition(SessionPhase.Starting);
            }
            else if (_room.Phase == SessionPhase.InGame &&
                     (Phase == SessionPhase.Ready || Phase == SessionPhase.Starting ||
                      Phase == SessionPhase.Synchronizing))
            {
                Transition(SessionPhase.InGame);
            }
            else if (_room.Phase == SessionPhase.Ready && Phase == SessionPhase.Starting)
            {
                Transition(SessionPhase.Ready);
            }
            else if (_room.Phase == SessionPhase.Failed &&
                     MultiplayerSessionStateMachine.CanTransition(Phase, SessionPhase.Failed))
            {
                Transition(SessionPhase.Failed);
                SetLastResult(OperationResult.Failure(
                    MultiplayerErrorCode.SynchronizationFailed,
                    "multiplayer.error.remote_session_failed"));
            }
        }

        private bool IsValidRemoteGenerationAdvance(RoomSnapshot incoming)
        {
            if (incoming == null || _room == null || _room.IsHost(_platform.LocalUser.Id) ||
                incoming.Id != _room.Id || incoming.SessionId != _room.SessionId ||
                incoming.SessionGeneration <= _room.SessionGeneration)
            {
                return false;
            }

            return incoming.Phase == SessionPhase.Starting ||
                   incoming.Phase == SessionPhase.InGame ||
                   incoming.Phase == SessionPhase.Ready ||
                   incoming.Phase == SessionPhase.Failed;
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

            if (connectionEvent.Type == ConnectionEventType.PeerConnected && _driver.IsServer)
            {
                MultiplayerPeer connectedPeer;
                if (TryGetPeer(connectionEvent.UserId, out connectedPeer))
                {
                    CancelPeerReconnectExpiry(connectedPeer.User.Id);
                    if (Phase == SessionPhase.InGame)
                    {
                        _ = SynchronizeConnectedPeerAsync(connectedPeer, true);
                    }
                    else if (Phase == SessionPhase.InRoom || Phase == SessionPhase.Ready)
                    {
                        _ = SynchronizeConnectedPeerAsync(connectedPeer, false);
                    }
                }

                return;
            }

            bool localIsHost = _room != null && _room.IsHost(_platform.LocalUser.Id);
            if (connectionEvent.Type == ConnectionEventType.LocalDisconnected &&
                Phase == SessionPhase.InGame && _config.ReconnectEnabled && !localIsHost)
            {
                _reconnectRemaining = _config.ReconnectHardDeadline;
                _reconnectAttempt = 0;
                Transition(SessionPhase.Reconnecting);
                SetLastResult(OperationResult.Failure(
                    MultiplayerErrorCode.TransportFailed,
                    string.IsNullOrWhiteSpace(connectionEvent.MessageKey)
                        ? "multiplayer.error.connection_lost"
                        : connectionEvent.MessageKey));
            }
            else if ((connectionEvent.Type == ConnectionEventType.LocalDisconnected ||
                      connectionEvent.Type == ConnectionEventType.Failed) &&
                     Phase == SessionPhase.InGame && localIsHost)
            {
                EnterTransportRecovery(connectionEvent);
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
                    MarkPeerDisconnected(peer);
                }
            }
        }

        private void EnterTransportRecovery(ConnectionEvent connectionEvent)
        {
            if (!MultiplayerSessionStateMachine.CanTransition(Phase, SessionPhase.Recovery))
            {
                return;
            }

            _reconnectRemaining = TimeSpan.Zero;
            _reconnectAttempt = 0;
            Transition(SessionPhase.Recovery);
            SetLastResult(OperationResult.Failure(
                connectionEvent.ErrorCode == MultiplayerErrorCode.None
                    ? MultiplayerErrorCode.TransportFailed
                    : connectionEvent.ErrorCode,
                string.IsNullOrWhiteSpace(connectionEvent.MessageKey)
                    ? "multiplayer.error.host_transport_failed"
                    : connectionEvent.MessageKey));
        }

        private async Task SynchronizeConnectedPeerAsync(MultiplayerPeer peer, bool restoring)
        {
            try
            {
                if (restoring)
                {
                    await RestorePeerAsync(peer, CancellationToken.None);
                }
                else
                {
                    await SynchronizePeerAsync(peer, CancellationToken.None);
                }
            }
            catch (Exception exception)
            {
                SetLastResult(OperationResult.Failure(
                    MultiplayerErrorCode.SynchronizationFailed,
                    "multiplayer.error.peer_sync_exception",
                    exception.GetType().Name));
            }
        }

        private void MarkPeerDisconnected(MultiplayerPeer peer)
        {
            if (_room == null || Phase != SessionPhase.InGame)
            {
                return;
            }

            _room = _room.WithMemberPhase(peer.User.Id, MemberConnectionPhase.Disconnected);
            OperationResult publish = PublishCurrentRoomState();
            if (!publish.Succeeded)
            {
                SetLastResult(publish);
            }

            _game.OnPeerDisconnected(peer, _config.ReconnectGracePeriod);
            SchedulePeerReconnectExpiry(peer);
            NotifyChanged();
        }

        private void SchedulePeerReconnectExpiry(MultiplayerPeer peer)
        {
            CancelPeerReconnectExpiry(peer.User.Id);
            var cancellation = new CancellationTokenSource();
            _peerReconnectExpiry[peer.User.Id] = cancellation;
            _ = ExpirePeerReconnectAfterDelayAsync(
                peer,
                _room == null ? default(SessionId) : _room.SessionId,
                _room == null ? 0 : _room.SessionGeneration,
                cancellation);
        }

        private async Task ExpirePeerReconnectAfterDelayAsync(
            MultiplayerPeer peer,
            SessionId sessionId,
            long sessionGeneration,
            CancellationTokenSource cancellation)
        {
            try
            {
                await Task.Delay(_config.ReconnectGracePeriod, cancellation.Token);
                if (!cancellation.IsCancellationRequested && _room != null &&
                    _room.SessionId == sessionId && _room.SessionGeneration == sessionGeneration &&
                    Phase == SessionPhase.InGame)
                {
                    ExpirePeerReconnect(peer);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (_peerReconnectExpiry.TryGetValue(peer.User.Id, out CancellationTokenSource current) &&
                    ReferenceEquals(current, cancellation))
                {
                    _peerReconnectExpiry.Remove(peer.User.Id);
                }

                cancellation.Dispose();
            }
        }

        private void CancelPeerReconnectExpiry(PlatformUserId userId)
        {
            if (_peerReconnectExpiry.TryGetValue(userId, out CancellationTokenSource cancellation))
            {
                _peerReconnectExpiry.Remove(userId);
                cancellation.Cancel();
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

            if (Phase == SessionPhase.Reconnecting ||
                MultiplayerSessionStateMachine.CanTransition(Phase, SessionPhase.Recovery))
            {
                _reconnectRemaining = TimeSpan.Zero;
                _reconnectAttempt = 0;
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
