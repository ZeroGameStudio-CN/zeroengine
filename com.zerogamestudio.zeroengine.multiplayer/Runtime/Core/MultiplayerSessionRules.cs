using System;
using System.Collections.Generic;
using System.Threading;

namespace ZeroEngine.Multiplayer
{
    public sealed class MultiplayerSessionStateMachine
    {
        public MultiplayerSessionStateMachine(SessionPhase initialPhase = SessionPhase.Offline)
        {
            Phase = initialPhase;
        }

        public SessionPhase Phase { get; private set; }

        public event Action<SessionPhase, SessionPhase> PhaseChanged;

        public OperationResult TryTransition(SessionPhase target)
        {
            SessionPhase source = Phase;
            if (!CanTransition(source, target))
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.InvalidState,
                    "multiplayer.error.invalid_transition",
                    source.ToString(),
                    target.ToString());
            }

            Phase = target;
            Action<SessionPhase, SessionPhase> handler = PhaseChanged;
            if (handler != null)
            {
                handler(source, target);
            }

            return OperationResult.Success();
        }

        public static bool CanTransition(SessionPhase source, SessionPhase target)
        {
            switch (source)
            {
                case SessionPhase.Offline:
                    return target == SessionPhase.Initializing;
                case SessionPhase.Initializing:
                    return target == SessionPhase.Idle || target == SessionPhase.Failed;
                case SessionPhase.Idle:
                    return target == SessionPhase.CreatingRoom || target == SessionPhase.JoiningRoom;
                case SessionPhase.CreatingRoom:
                    return target == SessionPhase.Connecting || target == SessionPhase.Failed;
                case SessionPhase.JoiningRoom:
                    return target == SessionPhase.Connecting || target == SessionPhase.Failed;
                case SessionPhase.Connecting:
                    return target == SessionPhase.InRoom || target == SessionPhase.Synchronizing ||
                           target == SessionPhase.Recovery || target == SessionPhase.Failed;
                case SessionPhase.InRoom:
                    return target == SessionPhase.Synchronizing || target == SessionPhase.Recovery ||
                           target == SessionPhase.Leaving;
                case SessionPhase.Synchronizing:
                    return target == SessionPhase.Ready || target == SessionPhase.InRoom ||
                           target == SessionPhase.InGame || target == SessionPhase.Reconnecting ||
                           target == SessionPhase.Recovery || target == SessionPhase.Failed;
                case SessionPhase.Ready:
                    return target == SessionPhase.Starting || target == SessionPhase.InRoom ||
                           target == SessionPhase.InGame || target == SessionPhase.Recovery ||
                           target == SessionPhase.Leaving || target == SessionPhase.Failed;
                case SessionPhase.Starting:
                    return target == SessionPhase.InGame || target == SessionPhase.Ready ||
                           target == SessionPhase.Recovery || target == SessionPhase.Failed;
                case SessionPhase.InGame:
                    return target == SessionPhase.Reconnecting || target == SessionPhase.Recovery ||
                           target == SessionPhase.Leaving;
                case SessionPhase.Reconnecting:
                    return target == SessionPhase.Synchronizing || target == SessionPhase.Recovery;
                case SessionPhase.Recovery:
                    return target == SessionPhase.Leaving;
                case SessionPhase.Failed:
                    return target == SessionPhase.CreatingRoom || target == SessionPhase.JoiningRoom ||
                           target == SessionPhase.Idle || target == SessionPhase.Leaving;
                case SessionPhase.Leaving:
                    return target == SessionPhase.Idle;
                default:
                    return false;
            }
        }
    }

    public static class CompatibilityValidator
    {
        public static OperationResult ValidateDescriptor(
            CompatibilityDescriptor descriptor,
            string protocolVersion)
        {
            if (string.IsNullOrWhiteSpace(descriptor.ProductId) ||
                string.IsNullOrWhiteSpace(descriptor.GameProtocolVersion) ||
                string.IsNullOrWhiteSpace(descriptor.ContentRevision) ||
                string.IsNullOrWhiteSpace(descriptor.BuildVersion) ||
                string.IsNullOrWhiteSpace(descriptor.GameRoomId) ||
                string.IsNullOrWhiteSpace(protocolVersion))
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.InvalidConfiguration,
                    "multiplayer.error.compatibility_descriptor_incomplete");
            }

            return OperationResult.Success();
        }

        public static OperationResult Validate(
            RoomSnapshot room,
            CompatibilityDescriptor local,
            string protocolVersion,
            BuildMatchPolicy buildMatchPolicy)
        {
            if (room == null)
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.InvalidArgument,
                    "multiplayer.error.room_missing");
            }

            OperationResult descriptor = ValidateDescriptor(local, protocolVersion);
            if (!descriptor.Succeeded)
            {
                return descriptor;
            }

            OperationResult result = Match(
                local.ProductId,
                room.ProductId,
                MultiplayerErrorCode.ProductMismatch,
                "multiplayer.error.product_mismatch");
            if (!result.Succeeded)
            {
                return result;
            }

            result = Match(
                protocolVersion,
                room.ProtocolVersion,
                MultiplayerErrorCode.ProtocolMismatch,
                "multiplayer.error.protocol_mismatch");
            if (!result.Succeeded)
            {
                return result;
            }

            result = Match(
                local.GameProtocolVersion,
                room.GameProtocolVersion,
                MultiplayerErrorCode.GameProtocolMismatch,
                "multiplayer.error.game_protocol_mismatch");
            if (!result.Succeeded)
            {
                return result;
            }

            result = Match(
                local.ContentRevision,
                room.ContentRevision,
                MultiplayerErrorCode.ContentMismatch,
                "multiplayer.error.content_mismatch");
            if (!result.Succeeded)
            {
                return result;
            }

            if (buildMatchPolicy == BuildMatchPolicy.Exact)
            {
                result = Match(
                    local.BuildVersion,
                    room.BuildVersion,
                    MultiplayerErrorCode.BuildMismatch,
                    "multiplayer.error.build_mismatch");
                if (!result.Succeeded)
                {
                    return result;
                }
            }

            return Match(
                local.GameRoomId,
                room.GameRoomId,
                MultiplayerErrorCode.GameRoomMismatch,
                "multiplayer.error.game_room_mismatch");
        }

        private static OperationResult Match(
            string local,
            string remote,
            MultiplayerErrorCode errorCode,
            string messageKey)
        {
            return string.Equals(local, remote, StringComparison.Ordinal)
                ? OperationResult.Success()
                : OperationResult.Failure(errorCode, messageKey, local ?? string.Empty, remote ?? string.Empty);
        }
    }

    public sealed class OperationGenerationGate
    {
        private int _generation;

        public int Current => Volatile.Read(ref _generation);

        public int Begin()
        {
            return Interlocked.Increment(ref _generation);
        }

        public int Invalidate()
        {
            return Interlocked.Increment(ref _generation);
        }

        public bool IsCurrent(int generation)
        {
            return generation == Current;
        }
    }

    public sealed class InviteRouter
    {
        private JoinRequest _pending;

        public bool HasPending { get; private set; }
        public JoinRequest Pending => _pending;

        public InviteRouteAction Route(JoinRequest request, SessionPhase phase, RoomSnapshot currentRoom)
        {
            if ((currentRoom != null && currentRoom.Id == request.RoomId) ||
                (HasPending && _pending.RoomId == request.RoomId))
            {
                return InviteRouteAction.IgnoreDuplicate;
            }

            _pending = request;
            HasPending = true;

            switch (phase)
            {
                case SessionPhase.Idle:
                case SessionPhase.Failed:
                case SessionPhase.Recovery:
                    return InviteRouteAction.Present;
                case SessionPhase.InRoom:
                case SessionPhase.Ready:
                    return InviteRouteAction.ConfirmLeaveCurrentSession;
                default:
                    return InviteRouteAction.QueueUntilStable;
            }
        }

        public bool TryTakePending(out JoinRequest request)
        {
            request = _pending;
            if (!HasPending)
            {
                return false;
            }

            Clear();
            return true;
        }

        public void Clear()
        {
            _pending = default(JoinRequest);
            HasPending = false;
        }
    }

    public sealed class ReconnectPolicy
    {
        private readonly TimeSpan[] _delays;

        public ReconnectPolicy(
            int maxAttempts,
            TimeSpan attemptTimeout,
            IReadOnlyList<TimeSpan> delays,
            TimeSpan hardDeadline,
            TimeSpan gracePeriod)
        {
            if (maxAttempts < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxAttempts));
            }

            if (attemptTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(attemptTimeout));
            }

            if (delays == null || delays.Count < maxAttempts)
            {
                throw new ArgumentException("A delay is required for every reconnect attempt.", nameof(delays));
            }

            if (hardDeadline <= TimeSpan.Zero || gracePeriod < hardDeadline)
            {
                throw new ArgumentOutOfRangeException(nameof(hardDeadline));
            }

            MaxAttempts = maxAttempts;
            AttemptTimeout = attemptTimeout;
            HardDeadline = hardDeadline;
            GracePeriod = gracePeriod;
            _delays = new TimeSpan[maxAttempts];

            TimeSpan worstCase = TimeSpan.Zero;
            for (int i = 0; i < maxAttempts; i++)
            {
                if (delays[i] < TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(delays));
                }

                _delays[i] = delays[i];
                worstCase += delays[i] + attemptTimeout;
            }

            if (worstCase > hardDeadline)
            {
                throw new ArgumentException("Reconnect schedule exceeds the hard deadline.", nameof(delays));
            }
        }

        public int MaxAttempts { get; }
        public TimeSpan AttemptTimeout { get; }
        public TimeSpan HardDeadline { get; }
        public TimeSpan GracePeriod { get; }

        public ReconnectBlockReason Evaluate(
            int zeroBasedAttempt,
            TimeSpan elapsed,
            bool cancelled,
            out ReconnectAttempt attempt)
        {
            attempt = default(ReconnectAttempt);

            if (cancelled)
            {
                return ReconnectBlockReason.Cancelled;
            }

            if (elapsed >= GracePeriod)
            {
                return ReconnectBlockReason.GraceExpired;
            }

            if (elapsed >= HardDeadline)
            {
                return ReconnectBlockReason.HardDeadlineExceeded;
            }

            if (zeroBasedAttempt < 0 || zeroBasedAttempt >= MaxAttempts)
            {
                return ReconnectBlockReason.AttemptsExhausted;
            }

            TimeSpan delay = _delays[zeroBasedAttempt];
            TimeSpan remaining = HardDeadline - elapsed - delay;
            if (remaining <= TimeSpan.Zero)
            {
                return ReconnectBlockReason.HardDeadlineExceeded;
            }

            TimeSpan timeout = remaining < AttemptTimeout ? remaining : AttemptTimeout;
            attempt = new ReconnectAttempt(zeroBasedAttempt + 1, delay, timeout);
            return ReconnectBlockReason.None;
        }

        public TimeSpan GetRemainingGrace(TimeSpan elapsed)
        {
            return elapsed >= GracePeriod ? TimeSpan.Zero : GracePeriod - elapsed;
        }
    }

    public sealed class ReconnectSeatRegistry
    {
        private readonly Dictionary<int, SeatReservation> _seats = new Dictionary<int, SeatReservation>();

        public ReconnectSeatRegistry(SessionId sessionId, long sessionGeneration)
        {
            SessionId = sessionId;
            SessionGeneration = sessionGeneration;
        }

        public SessionId SessionId { get; }
        public long SessionGeneration { get; }

        public OperationResult Register(int seatIndex, PlatformUserId ownerId)
        {
            if (seatIndex < 0 || ownerId.IsEmpty)
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.InvalidArgument,
                    "multiplayer.error.seat_registration_invalid");
            }

            if (_seats.ContainsKey(seatIndex))
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.SeatClaimed,
                    "multiplayer.error.seat_already_registered",
                    seatIndex.ToString());
            }

            _seats.Add(seatIndex, new SeatReservation(ownerId));
            return OperationResult.Success();
        }

        public OperationResult MarkDisconnected(PlatformUserId ownerId)
        {
            foreach (KeyValuePair<int, SeatReservation> pair in _seats)
            {
                if (pair.Value.OwnerId == ownerId)
                {
                    pair.Value.IsDisconnected = true;
                    return OperationResult.Success();
                }
            }

            return OperationResult.Failure(
                MultiplayerErrorCode.UnauthorizedPeer,
                "multiplayer.error.seat_owner_unknown");
        }

        public OperationResult<int> TryReclaim(
            int seatIndex,
            PlatformUserId claimantId,
            SessionId sessionId,
            long sessionGeneration)
        {
            if (sessionId != SessionId || sessionGeneration != SessionGeneration)
            {
                return OperationResult<int>.Failure(
                    MultiplayerErrorCode.SessionMismatch,
                    "multiplayer.error.reconnect_session_mismatch");
            }

            SeatReservation seat;
            if (!_seats.TryGetValue(seatIndex, out seat) || seat.OwnerId != claimantId)
            {
                return OperationResult<int>.Failure(
                    MultiplayerErrorCode.UnauthorizedPeer,
                    "multiplayer.error.reconnect_identity_mismatch");
            }

            if (!seat.IsDisconnected)
            {
                return OperationResult<int>.Failure(
                    MultiplayerErrorCode.SeatClaimed,
                    "multiplayer.error.seat_still_connected");
            }

            seat.IsDisconnected = false;
            return OperationResult<int>.Success(seatIndex);
        }

        private sealed class SeatReservation
        {
            public SeatReservation(PlatformUserId ownerId)
            {
                OwnerId = ownerId;
            }

            public PlatformUserId OwnerId { get; }
            public bool IsDisconnected { get; set; }
        }
    }

    public readonly struct MultiplayerSessionSnapshot
    {
        public MultiplayerSessionSnapshot(
            SessionPhase phase,
            RoomSnapshot room,
            PlatformUser localUser,
            bool isServer,
            bool operationInProgress,
            RetryOperationKind retryOperation,
            OperationResult lastResult,
            TimeSpan reconnectRemaining,
            int reconnectAttempt = 0)
        {
            Phase = phase;
            Room = room;
            LocalUser = localUser;
            IsServer = isServer;
            OperationInProgress = operationInProgress;
            RetryOperation = retryOperation;
            LastResult = lastResult;
            ReconnectRemaining = reconnectRemaining;
            ReconnectAttempt = reconnectAttempt;
        }

        public SessionPhase Phase { get; }
        public RoomSnapshot Room { get; }
        public PlatformUser LocalUser { get; }
        public bool IsServer { get; }
        public bool OperationInProgress { get; }
        public RetryOperationKind RetryOperation { get; }
        public OperationResult LastResult { get; }
        public TimeSpan ReconnectRemaining { get; }
        public int ReconnectAttempt { get; }
    }
}
