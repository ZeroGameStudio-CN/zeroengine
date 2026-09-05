using System;
using System.Collections.Generic;

namespace ZeroEngine.Multiplayer
{
    public readonly struct PlatformUserId : IEquatable<PlatformUserId>
    {
        public PlatformUserId(string value)
        {
            Value = value ?? string.Empty;
        }

        public string Value { get; }
        public bool IsEmpty => string.IsNullOrEmpty(Value);

        public bool Equals(PlatformUserId other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is PlatformUserId && Equals((PlatformUserId)obj);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        }

        public override string ToString()
        {
            return Value ?? string.Empty;
        }

        public static bool operator ==(PlatformUserId left, PlatformUserId right) => left.Equals(right);
        public static bool operator !=(PlatformUserId left, PlatformUserId right) => !left.Equals(right);
    }

    public readonly struct RoomId : IEquatable<RoomId>
    {
        public RoomId(string value)
        {
            Value = value ?? string.Empty;
        }

        public string Value { get; }
        public bool IsEmpty => string.IsNullOrEmpty(Value);
        public bool Equals(RoomId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is RoomId && Equals((RoomId)obj);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(RoomId left, RoomId right) => left.Equals(right);
        public static bool operator !=(RoomId left, RoomId right) => !left.Equals(right);
    }

    public readonly struct SessionId : IEquatable<SessionId>
    {
        public SessionId(string value)
        {
            Value = value ?? string.Empty;
        }

        public string Value { get; }
        public bool IsEmpty => string.IsNullOrEmpty(Value);
        public bool Equals(SessionId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is SessionId && Equals((SessionId)obj);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(SessionId left, SessionId right) => left.Equals(right);
        public static bool operator !=(SessionId left, SessionId right) => !left.Equals(right);
    }

    public enum RoomVisibility
    {
        Private,
        FriendsOnly,
        Public
    }

    public enum SessionPhase
    {
        Offline,
        Initializing,
        Idle,
        CreatingRoom,
        JoiningRoom,
        InRoom,
        Connecting,
        Synchronizing,
        Ready,
        Starting,
        InGame,
        Reconnecting,
        Recovery,
        Leaving,
        Failed
    }

    public enum MemberConnectionPhase
    {
        LobbyOnly,
        Connecting,
        Connected,
        Synchronizing,
        Ready,
        Disconnected
    }

    public enum ConnectionPhase
    {
        Stopped,
        StartingHost,
        Hosting,
        StartingClient,
        Connected,
        Stopping,
        Failed
    }

    public enum RoomEventType
    {
        RoomUpdated,
        MemberJoined,
        MemberLeft,
        HostLeft,
        DataChanged,
        Closed
    }

    public enum ConnectionEventType
    {
        HostStarted,
        ClientConnected,
        PeerConnected,
        PeerDisconnected,
        LocalDisconnected,
        Stopped,
        Failed
    }

    public enum DisconnectIntent
    {
        IntentionalLeave,
        Retry,
        Shutdown,
        HostLeft,
        Failure
    }

    public enum SessionEndReason
    {
        IntentionalLeave,
        HostLeft,
        ReconnectExpired,
        Failure,
        Shutdown
    }

    public enum TransportMode
    {
        SteamP2P,
        LocalDirect
    }

    public enum BuildMatchPolicy
    {
        Exact,
        Ignore
    }

    public enum MultiplayerLogLevel
    {
        None,
        Error,
        Info,
        Verbose
    }

    public enum MultiplayerErrorCode
    {
        None,
        InvalidConfiguration,
        InvalidState,
        InvalidArgument,
        PlatformUnavailable,
        RoomNotFound,
        Busy,
        Cancelled,
        Timeout,
        StaleOperation,
        CreateFailed,
        JoinFailed,
        LeaveFailed,
        TransportFailed,
        TransportUnavailable,
        ConnectionTimeout,
        IdentityRejected,
        SynchronizationFailed,
        RoomFull,
        RoomStarted,
        InviteExpired,
        ProductMismatch,
        ProtocolMismatch,
        GameProtocolMismatch,
        ContentMismatch,
        BuildMismatch,
        GameRoomMismatch,
        UnauthorizedPeer,
        SeatClaimed,
        SessionMismatch,
        HostLeft,
        ReconnectExpired,
        Unknown
    }

    public enum InviteRouteAction
    {
        Present,
        ConfirmLeaveCurrentSession,
        QueueUntilStable,
        IgnoreDuplicate
    }

    public enum ReconnectBlockReason
    {
        None,
        Cancelled,
        AttemptsExhausted,
        HardDeadlineExceeded,
        GraceExpired
    }

    public enum RetryOperationKind
    {
        None,
        CreateRoom,
        JoinRoom
    }

    public readonly struct PlatformUser
    {
        public PlatformUser(PlatformUserId id, string displayName)
        {
            Id = id;
            DisplayName = displayName ?? string.Empty;
        }

        public PlatformUserId Id { get; }
        public string DisplayName { get; }
    }

    public readonly struct RoomMember
    {
        public RoomMember(PlatformUser user, MemberConnectionPhase connectionPhase, bool isHost, int seatIndex)
        {
            User = user;
            ConnectionPhase = connectionPhase;
            IsHost = isHost;
            SeatIndex = seatIndex;
        }

        public PlatformUser User { get; }
        public MemberConnectionPhase ConnectionPhase { get; }
        public bool IsHost { get; }
        public int SeatIndex { get; }

        public RoomMember WithConnectionPhase(MemberConnectionPhase phase)
        {
            return new RoomMember(User, phase, IsHost, SeatIndex);
        }
    }

    public readonly struct CompatibilityDescriptor
    {
        public CompatibilityDescriptor(
            string productId,
            string gameProtocolVersion,
            string contentRevision,
            string buildVersion,
            string gameRoomId)
        {
            ProductId = productId ?? string.Empty;
            GameProtocolVersion = gameProtocolVersion ?? string.Empty;
            ContentRevision = contentRevision ?? string.Empty;
            BuildVersion = buildVersion ?? string.Empty;
            GameRoomId = gameRoomId ?? string.Empty;
        }

        public string ProductId { get; }
        public string GameProtocolVersion { get; }
        public string ContentRevision { get; }
        public string BuildVersion { get; }
        public string GameRoomId { get; }
    }

    public readonly struct RoomCreateOptions
    {
        public RoomCreateOptions(
            RoomVisibility visibility,
            int maxMembers,
            bool allowJoinInProgress,
            string protocolVersion,
            CompatibilityDescriptor compatibility)
        {
            Visibility = visibility;
            MaxMembers = maxMembers;
            AllowJoinInProgress = allowJoinInProgress;
            ProtocolVersion = protocolVersion ?? string.Empty;
            Compatibility = compatibility;
        }

        public RoomVisibility Visibility { get; }
        public int MaxMembers { get; }
        public bool AllowJoinInProgress { get; }
        public string ProtocolVersion { get; }
        public CompatibilityDescriptor Compatibility { get; }
    }

    public sealed class RoomSnapshot
    {
        private readonly IReadOnlyList<RoomMember> _members;

        public RoomSnapshot(
            RoomId id,
            PlatformUserId hostId,
            IEnumerable<RoomMember> members,
            int maxMembers,
            RoomVisibility visibility,
            bool isJoinable,
            string productId,
            string protocolVersion,
            string gameProtocolVersion,
            string contentRevision,
            string buildVersion,
            string gameRoomId,
            SessionId sessionId,
            long sessionGeneration,
            SessionPhase phase,
            bool hasStarted,
            string connectionAddress = "")
        {
            Id = id;
            HostId = hostId;
            _members = new List<RoomMember>(members ?? Array.Empty<RoomMember>()).AsReadOnly();
            MaxMembers = maxMembers;
            Visibility = visibility;
            IsJoinable = isJoinable;
            ProductId = productId ?? string.Empty;
            ProtocolVersion = protocolVersion ?? string.Empty;
            GameProtocolVersion = gameProtocolVersion ?? string.Empty;
            ContentRevision = contentRevision ?? string.Empty;
            BuildVersion = buildVersion ?? string.Empty;
            GameRoomId = gameRoomId ?? string.Empty;
            SessionId = sessionId;
            SessionGeneration = sessionGeneration;
            Phase = phase;
            HasStarted = hasStarted;
            ConnectionAddress = connectionAddress ?? string.Empty;
        }

        public RoomId Id { get; }
        public PlatformUserId HostId { get; }
        public IReadOnlyList<RoomMember> Members => _members;
        public int MaxMembers { get; }
        public RoomVisibility Visibility { get; }
        public bool IsJoinable { get; }
        public string ProductId { get; }
        public string ProtocolVersion { get; }
        public string GameProtocolVersion { get; }
        public string ContentRevision { get; }
        public string BuildVersion { get; }
        public string GameRoomId { get; }
        public SessionId SessionId { get; }
        public long SessionGeneration { get; }
        public SessionPhase Phase { get; }
        public bool HasStarted { get; }
        public string ConnectionAddress { get; }

        public bool IsHost(PlatformUserId userId)
        {
            return HostId == userId;
        }

        public RoomSnapshot WithSession(long generation, SessionPhase phase, bool hasStarted, bool isJoinable)
        {
            return new RoomSnapshot(
                Id,
                HostId,
                _members,
                MaxMembers,
                Visibility,
                isJoinable,
                ProductId,
                ProtocolVersion,
                GameProtocolVersion,
                ContentRevision,
                BuildVersion,
                GameRoomId,
                SessionId,
                generation,
                phase,
                hasStarted,
                ConnectionAddress);
        }

        public RoomSnapshot WithMemberPhase(PlatformUserId userId, MemberConnectionPhase phase)
        {
            List<RoomMember> members = new List<RoomMember>(_members.Count);
            for (int i = 0; i < _members.Count; i++)
            {
                RoomMember member = _members[i];
                members.Add(member.User.Id == userId ? member.WithConnectionPhase(phase) : member);
            }

            return new RoomSnapshot(
                Id,
                HostId,
                members,
                MaxMembers,
                Visibility,
                IsJoinable,
                ProductId,
                ProtocolVersion,
                GameProtocolVersion,
                ContentRevision,
                BuildVersion,
                GameRoomId,
                SessionId,
                SessionGeneration,
                Phase,
                HasStarted,
                ConnectionAddress);
        }
    }

    public readonly struct JoinRequest
    {
        public JoinRequest(RoomId roomId, PlatformUser sender)
        {
            RoomId = roomId;
            Sender = sender;
        }

        public RoomId RoomId { get; }
        public PlatformUser Sender { get; }
    }

    public readonly struct PlatformRoomEvent
    {
        public PlatformRoomEvent(
            RoomEventType type,
            RoomId roomId,
            long sessionGeneration,
            RoomSnapshot snapshot,
            PlatformUserId memberId)
        {
            Type = type;
            RoomId = roomId;
            SessionGeneration = sessionGeneration;
            Snapshot = snapshot;
            MemberId = memberId;
        }

        public RoomEventType Type { get; }
        public RoomId RoomId { get; }
        public long SessionGeneration { get; }
        public RoomSnapshot Snapshot { get; }
        public PlatformUserId MemberId { get; }
    }

    public readonly struct HostConnectionOptions
    {
        public HostConnectionOptions(RoomId roomId, SessionId sessionId, long sessionGeneration)
        {
            RoomId = roomId;
            SessionId = sessionId;
            SessionGeneration = sessionGeneration;
        }

        public RoomId RoomId { get; }
        public SessionId SessionId { get; }
        public long SessionGeneration { get; }
    }

    public readonly struct ClientConnectionOptions
    {
        public ClientConnectionOptions(
            RoomId roomId,
            PlatformUserId hostId,
            SessionId sessionId,
            long sessionGeneration,
            string connectionAddress = "")
        {
            RoomId = roomId;
            HostId = hostId;
            SessionId = sessionId;
            SessionGeneration = sessionGeneration;
            ConnectionAddress = connectionAddress ?? string.Empty;
        }

        public RoomId RoomId { get; }
        public PlatformUserId HostId { get; }
        public SessionId SessionId { get; }
        public long SessionGeneration { get; }
        public string ConnectionAddress { get; }
    }

    public readonly struct ConnectionEvent
    {
        public ConnectionEvent(
            ConnectionEventType type,
            PlatformUserId userId,
            MultiplayerErrorCode errorCode,
            string messageKey)
        {
            Type = type;
            UserId = userId;
            ErrorCode = errorCode;
            MessageKey = messageKey ?? string.Empty;
        }

        public ConnectionEventType Type { get; }
        public PlatformUserId UserId { get; }
        public MultiplayerErrorCode ErrorCode { get; }
        public string MessageKey { get; }
    }

    public readonly struct MultiplayerSessionContext
    {
        public MultiplayerSessionContext(RoomSnapshot room, PlatformUser localUser, bool isHost)
        {
            Room = room;
            LocalUser = localUser;
            IsHost = isHost;
        }

        public RoomSnapshot Room { get; }
        public PlatformUser LocalUser { get; }
        public bool IsHost { get; }
    }

    public readonly struct MultiplayerPeer
    {
        public MultiplayerPeer(PlatformUser user, int seatIndex)
        {
            User = user;
            SeatIndex = seatIndex;
        }

        public PlatformUser User { get; }
        public int SeatIndex { get; }
    }

    public readonly struct ReconnectAttempt
    {
        public ReconnectAttempt(int number, TimeSpan delay, TimeSpan timeout)
        {
            Number = number;
            Delay = delay;
            Timeout = timeout;
        }

        public int Number { get; }
        public TimeSpan Delay { get; }
        public TimeSpan Timeout { get; }
    }
}
