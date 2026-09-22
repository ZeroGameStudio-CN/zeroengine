using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.Multiplayer.Tests
{
    internal sealed class FakePlatformRoomService : IPlatformRoomService, IRoomStatePublisher
    {
        public bool IsAvailable { get; set; } = true;
        public string UnavailableReasonKey { get; set; } = string.Empty;
        public PlatformUser LocalUser { get; set; } = new PlatformUser(new PlatformUserId("local"), "Local");
        public RoomSnapshot CurrentRoom { get; private set; }
        public OperationResult<RoomSnapshot> CreateResult { get; set; }
        public OperationResult<RoomSnapshot> JoinResult { get; set; }
        public OperationResult LeaveResult { get; set; } = OperationResult.Success();
        public OperationResult InviteResult { get; set; } = OperationResult.Success();
        public OperationResult PublishResult { get; set; } = OperationResult.Success();
        public Func<RoomCreateOptions, CancellationToken, Task<OperationResult<RoomSnapshot>>> CreateHandler { get; set; }
        public Func<RoomId, CancellationToken, Task<OperationResult<RoomSnapshot>>> JoinHandler { get; set; }
        public int CreateCalls { get; private set; }
        public int JoinCalls { get; private set; }
        public int LeaveCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public int PublishCalls { get; private set; }
        public List<RoomSnapshot> PublishedRooms { get; } = new List<RoomSnapshot>();

        public event Action<PlatformRoomEvent> RoomEvent;
        public event Action<JoinRequest> JoinRequested;

        public async Task<OperationResult<RoomSnapshot>> CreateAsync(
            RoomCreateOptions options,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            OperationResult<RoomSnapshot> result = CreateHandler == null
                ? CreateResult
                : await CreateHandler(options, cancellationToken);
            if (result.Succeeded)
            {
                CurrentRoom = result.Value;
            }

            return result;
        }

        public async Task<OperationResult<RoomSnapshot>> JoinAsync(
            RoomId roomId,
            CancellationToken cancellationToken)
        {
            JoinCalls++;
            OperationResult<RoomSnapshot> result = JoinHandler == null
                ? JoinResult
                : await JoinHandler(roomId, cancellationToken);
            if (result.Succeeded)
            {
                CurrentRoom = result.Value;
            }

            return result;
        }

        public Task<OperationResult> LeaveAsync(CancellationToken cancellationToken)
        {
            LeaveCalls++;
            CurrentRoom = null;
            return Task.FromResult(LeaveResult);
        }

        public OperationResult OpenInviteOverlay()
        {
            return InviteResult;
        }

        public Task<OperationResult<RoomSnapshot>> RefreshAsync(CancellationToken cancellationToken)
        {
            return CurrentRoom == null
                ? Task.FromResult(OperationResult<RoomSnapshot>.Failure(
                    MultiplayerErrorCode.InvalidState,
                    "test.no_room"))
                : Task.FromResult(OperationResult<RoomSnapshot>.Success(CurrentRoom));
        }

        public OperationResult PublishRoomState(RoomSnapshot room)
        {
            PublishCalls++;
            PublishedRooms.Add(room);
            if (PublishResult.Succeeded)
            {
                CurrentRoom = room;
            }

            return PublishResult;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return default(ValueTask);
        }

        public void EmitRoomEvent(PlatformRoomEvent roomEvent)
        {
            if (roomEvent.Snapshot != null)
            {
                CurrentRoom = roomEvent.Snapshot;
            }

            Action<PlatformRoomEvent> handler = RoomEvent;
            if (handler != null)
            {
                handler(roomEvent);
            }
        }

        public void EmitJoinRequest(JoinRequest request)
        {
            Action<JoinRequest> handler = JoinRequested;
            if (handler != null)
            {
                handler(request);
            }
        }
    }

    internal sealed class FakeConnectionDriver : INetworkConnectionDriver
    {
        public ConnectionPhase Phase { get; private set; } = ConnectionPhase.Stopped;
        public bool IsServer { get; private set; }
        public bool IsClient { get; private set; }
        public OperationResult StartHostResult { get; set; } = OperationResult.Success();
        public OperationResult StartClientResult { get; set; } = OperationResult.Success();
        public OperationResult StopResult { get; set; } = OperationResult.Success();
        public bool EmitDisconnectOnStop { get; set; }
        public int StartHostCalls { get; private set; }
        public int StartClientCalls { get; private set; }
        public int StopCalls { get; private set; }

        public event Action<ConnectionEvent> ConnectionEvent;

        public Task<OperationResult> StartHostAsync(
            HostConnectionOptions options,
            CancellationToken cancellationToken)
        {
            StartHostCalls++;
            if (StartHostResult.Succeeded)
            {
                Phase = ConnectionPhase.Hosting;
                IsServer = true;
                IsClient = true;
            }
            else
            {
                Phase = ConnectionPhase.Failed;
            }

            return Task.FromResult(StartHostResult);
        }

        public Task<OperationResult> StartClientAsync(
            ClientConnectionOptions options,
            CancellationToken cancellationToken)
        {
            StartClientCalls++;
            if (StartClientResult.Succeeded)
            {
                Phase = ConnectionPhase.Connected;
                IsClient = true;
                IsServer = false;
            }
            else
            {
                Phase = ConnectionPhase.Failed;
            }

            return Task.FromResult(StartClientResult);
        }

        public Task<OperationResult> StopAsync(
            DisconnectIntent intent,
            CancellationToken cancellationToken)
        {
            StopCalls++;
            if (EmitDisconnectOnStop)
            {
                Emit(new ConnectionEvent(
                    ConnectionEventType.LocalDisconnected,
                    default(PlatformUserId),
                    MultiplayerErrorCode.TransportFailed,
                    "test.disconnect_on_stop"));
            }

            Phase = StopResult.Succeeded ? ConnectionPhase.Stopped : ConnectionPhase.Failed;
            IsServer = false;
            IsClient = false;
            return Task.FromResult(StopResult);
        }

        public void Emit(ConnectionEvent connectionEvent)
        {
            if (connectionEvent.Type == ConnectionEventType.LocalDisconnected ||
                connectionEvent.Type == ConnectionEventType.Failed)
            {
                Phase = ConnectionPhase.Failed;
                IsClient = false;
                if (connectionEvent.Type == ConnectionEventType.Failed)
                {
                    IsServer = false;
                }
            }

            Action<ConnectionEvent> handler = ConnectionEvent;
            if (handler != null)
            {
                handler(connectionEvent);
            }
        }
    }

    internal sealed class FakeGameAdapter : IMultiplayerGameAdapter
    {
        private readonly Queue<OperationResult> _prepareResults = new Queue<OperationResult>();

        public CompatibilityDescriptor Compatibility { get; set; } = TestData.Compatibility;
        public OperationResult DefaultPrepareResult { get; set; } = OperationResult.Success();
        public OperationResult LocalSynchronizeResult { get; set; } = OperationResult.Success();
        public OperationResult SynchronizeResult { get; set; } = OperationResult.Success();
        public OperationResult RestoreResult { get; set; } = OperationResult.Success();
        public int PrepareCalls { get; private set; }
        public int LocalSynchronizeCalls { get; private set; }
        public int SynchronizeCalls { get; private set; }
        public int RestoreCalls { get; private set; }
        public int PeerDisconnectedCalls { get; private set; }
        public int ReconnectExpiredCalls { get; private set; }
        public int SessionEndedCalls { get; private set; }

        public CompatibilityDescriptor GetCompatibility()
        {
            return Compatibility;
        }

        public CompatibilityDescriptor GetCompatibility(string gameRoomId)
        {
            return Compatibility;
        }

        public Task<OperationResult> PrepareSessionAsync(
            MultiplayerSessionContext context,
            CancellationToken cancellationToken)
        {
            PrepareCalls++;
            return Task.FromResult(_prepareResults.Count > 0
                ? _prepareResults.Dequeue()
                : DefaultPrepareResult);
        }

        public Task<OperationResult> SynchronizeLocalAsync(
            MultiplayerSessionContext context,
            CancellationToken cancellationToken)
        {
            LocalSynchronizeCalls++;
            return Task.FromResult(LocalSynchronizeResult);
        }

        public Task<OperationResult> SynchronizePeerAsync(
            MultiplayerPeer peer,
            CancellationToken cancellationToken)
        {
            SynchronizeCalls++;
            return Task.FromResult(SynchronizeResult);
        }

        public Task<OperationResult> RestorePeerAsync(
            MultiplayerPeer peer,
            CancellationToken cancellationToken)
        {
            RestoreCalls++;
            return Task.FromResult(RestoreResult);
        }

        public void OnPeerDisconnected(MultiplayerPeer peer, TimeSpan gracePeriod)
        {
            PeerDisconnectedCalls++;
        }

        public void OnPeerReconnectExpired(MultiplayerPeer peer)
        {
            ReconnectExpiredCalls++;
        }

        public void OnSessionEnded(SessionEndReason reason)
        {
            SessionEndedCalls++;
        }

        public void EnqueuePrepare(OperationResult result)
        {
            _prepareResults.Enqueue(result);
        }
    }

    internal static class TestData
    {
        public static readonly PlatformUser Host = new PlatformUser(new PlatformUserId("host"), "Host");
        public static readonly PlatformUser Guest = new PlatformUser(new PlatformUserId("guest"), "Guest");
        public static readonly CompatibilityDescriptor Compatibility = new CompatibilityDescriptor(
            "test-product",
            "game-1",
            "content-1",
            "build-1",
            "gallery-room");

        public static RoomSnapshot CreateRoom(
            PlatformUser localUser,
            MemberConnectionPhase hostPhase = MemberConnectionPhase.Ready,
            MemberConnectionPhase guestPhase = MemberConnectionPhase.Ready,
            bool includeGuest = true,
            string productId = "test-product",
            string protocolVersion = "1",
            string gameProtocolVersion = "game-1",
            string contentRevision = "content-1",
            string buildVersion = "build-1",
            string gameRoomId = "gallery-room",
            long sessionGeneration = 1)
        {
            List<RoomMember> members = new List<RoomMember>
            {
                new RoomMember(Host, hostPhase, true, 0)
            };
            if (includeGuest)
            {
                members.Add(new RoomMember(Guest, guestPhase, false, 1));
            }

            return new RoomSnapshot(
                new RoomId("room-1"),
                Host.Id,
                members,
                2,
                RoomVisibility.FriendsOnly,
                true,
                productId,
                protocolVersion,
                gameProtocolVersion,
                contentRevision,
                buildVersion,
                gameRoomId,
                new SessionId("session-1"),
                sessionGeneration,
                SessionPhase.InRoom,
                false);
        }

        public static MultiplayerPeer GuestPeer()
        {
            return new MultiplayerPeer(Guest, 1);
        }
    }
}
