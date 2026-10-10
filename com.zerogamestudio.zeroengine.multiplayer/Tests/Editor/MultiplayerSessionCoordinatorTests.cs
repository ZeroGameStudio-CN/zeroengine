using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.Multiplayer.Tests
{
    public sealed class MultiplayerSessionCoordinatorTests
    {
        private MultiplayerSessionConfig _config;
        private FakePlatformRoomService _platform;
        private FakeConnectionDriver _driver;
        private FakeGameAdapter _game;
        private MultiplayerSessionCoordinator _coordinator;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<MultiplayerSessionConfig>();
            typeof(MultiplayerSessionConfig)
                .GetField(
                    "reconnectRetryIntervalsSeconds",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                .SetValue(_config, new[] { 0f, 0f, 0f });
            _platform = new FakePlatformRoomService
            {
                LocalUser = TestData.Host,
                CreateResult = OperationResult<RoomSnapshot>.Success(TestData.CreateRoom(TestData.Host)),
                JoinResult = OperationResult<RoomSnapshot>.Success(TestData.CreateRoom(TestData.Guest))
            };
            _driver = new FakeConnectionDriver();
            _game = new FakeGameAdapter();
            _coordinator = new MultiplayerSessionCoordinator(_config, _platform, _driver, _game);
        }

        [TearDown]
        public void TearDown()
        {
            if (_coordinator != null)
            {
                _coordinator.DisposeAsync().GetAwaiter().GetResult();
            }

            if (_config != null)
            {
                Object.DestroyImmediate(_config);
            }
        }

        [Test]
        public void CreateRoom_CompletesInStableHostRoom()
        {
            Assert.IsTrue(Complete(_coordinator.InitializeAsync(CancellationToken.None)).Succeeded);

            OperationResult<RoomSnapshot> result = Complete(
                _coordinator.CreateRoomAsync(CancellationToken.None));

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(SessionPhase.InRoom, _coordinator.Phase);
            Assert.AreSame(result.Value, _coordinator.CurrentRoom);
            Assert.AreEqual(1, _driver.StartHostCalls);
            Assert.AreEqual(1, _game.PrepareCalls);
            Assert.AreEqual(RetryOperationKind.None, _coordinator.GetSnapshot().RetryOperation);
            Assert.AreEqual(SessionPhase.InRoom, _platform.PublishedRooms[0].Phase);
        }

        [Test]
        public void ConcurrentHighLevelOperation_ReturnsBusyWithoutChangingActiveFlow()
        {
            Assert.IsTrue(Complete(_coordinator.InitializeAsync(CancellationToken.None)).Succeeded);
            TaskCompletionSource<OperationResult<RoomSnapshot>> completion =
                new TaskCompletionSource<OperationResult<RoomSnapshot>>();
            _platform.CreateHandler = (options, token) => completion.Task;

            Task<OperationResult<RoomSnapshot>> createTask =
                _coordinator.CreateRoomAsync(CancellationToken.None);
            Assert.AreEqual(1, _platform.CreateCalls);

            OperationResult<RoomSnapshot> concurrentJoin =
                Complete(_coordinator.JoinRoomAsync(new RoomId("other-room"), CancellationToken.None));

            Assert.IsFalse(concurrentJoin.Succeeded);
            Assert.AreEqual(MultiplayerErrorCode.Busy, concurrentJoin.ErrorCode);
            Assert.AreEqual(SessionPhase.CreatingRoom, _coordinator.Phase);

            completion.SetResult(OperationResult<RoomSnapshot>.Success(TestData.CreateRoom(TestData.Host)));
            Assert.IsTrue(Complete(createTask).Succeeded);
            Assert.AreEqual(SessionPhase.InRoom, _coordinator.Phase);
        }

        [Test]
        public void CreateFailure_EntersFailedAndRetainsRetryIntent()
        {
            _platform.CreateResult = OperationResult<RoomSnapshot>.Failure(
                MultiplayerErrorCode.CreateFailed,
                "test.create_failed");
            Assert.IsTrue(Complete(_coordinator.InitializeAsync(CancellationToken.None)).Succeeded);

            OperationResult<RoomSnapshot> failed = Complete(
                _coordinator.CreateRoomAsync(CancellationToken.None));

            Assert.IsFalse(failed.Succeeded);
            Assert.AreEqual(SessionPhase.Failed, _coordinator.Phase);
            Assert.AreEqual(RetryOperationKind.CreateRoom, _coordinator.GetSnapshot().RetryOperation);

            _platform.CreateResult = OperationResult<RoomSnapshot>.Success(TestData.CreateRoom(TestData.Host));
            OperationResult<RoomSnapshot> retry = Complete(
                _coordinator.RetryAsync(CancellationToken.None));

            Assert.IsTrue(retry.Succeeded);
            Assert.AreEqual(SessionPhase.InRoom, _coordinator.Phase);
        }

        [Test]
        public void SuccessfulPlatformResultWithoutRoom_EntersFailedInsteadOfThrowing()
        {
            _platform.CreateResult = OperationResult<RoomSnapshot>.Success(null);
            Assert.IsTrue(Complete(_coordinator.InitializeAsync(CancellationToken.None)).Succeeded);

            OperationResult<RoomSnapshot> result =
                Complete(_coordinator.CreateRoomAsync(CancellationToken.None));

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(MultiplayerErrorCode.CreateFailed, result.ErrorCode);
            Assert.AreEqual(SessionPhase.Failed, _coordinator.Phase);
            Assert.IsNull(_coordinator.CurrentRoom);
        }

        [Test]
        public void CreateRoom_WithIncompleteGameCompatibilityFailsBeforePlatformCall()
        {
            _game.Compatibility = new CompatibilityDescriptor(
                "test-product",
                string.Empty,
                "content-1",
                "build-1",
                "gallery-room");
            Assert.IsTrue(Complete(_coordinator.InitializeAsync(CancellationToken.None)).Succeeded);

            OperationResult<RoomSnapshot> result = Complete(
                _coordinator.CreateRoomAsync(CancellationToken.None));

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(MultiplayerErrorCode.InvalidConfiguration, result.ErrorCode);
            Assert.AreEqual(SessionPhase.Failed, _coordinator.Phase);
            Assert.AreEqual(0, _platform.CreateCalls);
        }

        [Test]
        public void JoinCompatibilityFailure_LeavesPlatformAndEntersFailed()
        {
            _platform.LocalUser = TestData.Guest;
            _platform.JoinResult = OperationResult<RoomSnapshot>.Success(
                TestData.CreateRoom(TestData.Guest, productId: "different-product"));
            Assert.IsTrue(Complete(_coordinator.InitializeAsync(CancellationToken.None)).Succeeded);

            OperationResult<RoomSnapshot> result = Complete(_coordinator.JoinRoomAsync(
                new RoomId("room-1"),
                CancellationToken.None));

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(MultiplayerErrorCode.ProductMismatch, result.ErrorCode);
            Assert.AreEqual(SessionPhase.Failed, _coordinator.Phase);
            Assert.AreEqual(1, _platform.LeaveCalls);
        }

        [Test]
        public void JoinNonJoinableRoom_FailsBeforeStartingTransport()
        {
            _platform.LocalUser = TestData.Guest;
            _platform.JoinResult = OperationResult<RoomSnapshot>.Success(
                TestData.CreateRoom(TestData.Guest).WithSession(
                    2,
                    SessionPhase.InGame,
                    true,
                    false));
            Assert.IsTrue(Complete(_coordinator.InitializeAsync(CancellationToken.None)).Succeeded);

            OperationResult<RoomSnapshot> result = Complete(_coordinator.JoinRoomAsync(
                new RoomId("room-1"),
                CancellationToken.None));

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(MultiplayerErrorCode.RoomStarted, result.ErrorCode);
            Assert.AreEqual(0, _driver.StartClientCalls);
            Assert.AreEqual(1, _platform.LeaveCalls);
        }

        [Test]
        public void JoinSuccess_PreparesConnectsAndSynchronizesBeforeReady()
        {
            _platform.LocalUser = TestData.Guest;
            Assert.IsTrue(Complete(_coordinator.InitializeAsync(CancellationToken.None)).Succeeded);

            OperationResult<RoomSnapshot> join = Complete(_coordinator.JoinRoomAsync(
                new RoomId("room-1"),
                CancellationToken.None));

            Assert.IsTrue(join.Succeeded);
            Assert.AreEqual(SessionPhase.Ready, _coordinator.Phase);
            Assert.AreEqual(1, _game.PrepareCalls);
            Assert.AreEqual(1, _game.LocalSynchronizeCalls);
        }

        [Test]
        public void ConfirmedInviteFromExistingRoom_LeavesThenJoinsRequestedRoom()
        {
            Assert.IsTrue(Complete(_coordinator.InitializeAsync(CancellationToken.None)).Succeeded);
            Assert.IsTrue(Complete(_coordinator.CreateRoomAsync(CancellationToken.None)).Succeeded);
            _platform.LocalUser = TestData.Guest;
            _platform.EmitJoinRequest(new JoinRequest(new RoomId("invited-room"), TestData.Host));

            OperationResult<RoomSnapshot> result =
                Complete(_coordinator.AcceptJoinRequestAsync(CancellationToken.None));

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(1, _platform.LeaveCalls);
            Assert.AreEqual(1, _platform.JoinCalls);
            Assert.AreEqual(SessionPhase.Ready, _coordinator.Phase);
        }

        [Test]
        public void RecoverableStartFailure_ReturnsReadyWithNewGeneration()
        {
            ReachReadyHost();
            long before = _coordinator.CurrentRoom.SessionGeneration;
            int publishCount = _platform.PublishedRooms.Count;
            _game.DefaultPrepareResult = OperationResult.Failure(
                MultiplayerErrorCode.SynchronizationFailed,
                "test.start_failed");

            OperationResult result = Complete(_coordinator.StartGameAsync(CancellationToken.None));

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(SessionPhase.Ready, _coordinator.Phase);
            Assert.AreEqual(before + 2, _coordinator.CurrentRoom.SessionGeneration);
            Assert.IsTrue(_coordinator.CurrentRoom.IsJoinable);
            Assert.AreEqual(SessionPhase.Starting, _platform.PublishedRooms[publishCount].Phase);
            Assert.AreEqual(before + 1, _platform.PublishedRooms[publishCount].SessionGeneration);
            Assert.AreEqual(SessionPhase.Ready, _platform.PublishedRooms[publishCount + 1].Phase);
            Assert.AreEqual(before + 2, _platform.PublishedRooms[publishCount + 1].SessionGeneration);
        }

        [Test]
        public void SuccessfulStart_PublishesStartingThenInGame()
        {
            ReachReadyHost();
            long before = _coordinator.CurrentRoom.SessionGeneration;
            int publishCount = _platform.PublishedRooms.Count;

            OperationResult result = Complete(_coordinator.StartGameAsync(CancellationToken.None));

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(SessionPhase.Starting, _platform.PublishedRooms[publishCount].Phase);
            Assert.IsFalse(_platform.PublishedRooms[publishCount].IsJoinable);
            Assert.AreEqual(SessionPhase.InGame, _platform.PublishedRooms[publishCount + 1].Phase);
            Assert.AreEqual(before + 1, _platform.PublishedRooms[publishCount + 1].SessionGeneration);
        }

        [Test]
        public void Start_WhenRoomStateCannotBePublished_EntersFailed()
        {
            ReachReadyHost();
            _platform.PublishResult = OperationResult.Failure(
                MultiplayerErrorCode.PlatformUnavailable,
                "test.publish_failed");

            OperationResult result = Complete(_coordinator.StartGameAsync(CancellationToken.None));

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(MultiplayerErrorCode.PlatformUnavailable, result.ErrorCode);
            Assert.AreEqual(SessionPhase.Failed, _coordinator.Phase);
            Assert.AreEqual(SessionPhase.Failed, _coordinator.CurrentRoom.Phase);
            Assert.IsFalse(_coordinator.CurrentRoom.IsJoinable);
        }

        [Test]
        public void IntentionalLeave_DoesNotEnterReconnectAndEndsIdleEvenWhenCleanupFails()
        {
            ReachInGameHost();
            _driver.EmitDisconnectOnStop = true;
            _driver.StopResult = OperationResult.Failure(
                MultiplayerErrorCode.LeaveFailed,
                "test.stop_failed");

            OperationResult result = Complete(_coordinator.LeaveAsync(CancellationToken.None));

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(SessionPhase.Idle, _coordinator.Phase);
            Assert.IsNull(_coordinator.CurrentRoom);
            Assert.AreEqual(1, _game.SessionEndedCalls);
        }

        [Test]
        public void UnexpectedDisconnect_EntersReconnectAndCanBeCancelledToRecovery()
        {
            ReachInGameGuest();

            _driver.Emit(new ConnectionEvent(
                ConnectionEventType.LocalDisconnected,
                TestData.Guest.Id,
                MultiplayerErrorCode.TransportFailed,
                "test.connection_lost"));
            _coordinator.UpdateReconnectElapsed(System.TimeSpan.FromSeconds(3));

            Assert.AreEqual(SessionPhase.Reconnecting, _coordinator.Phase);
            Assert.AreEqual(System.TimeSpan.FromSeconds(15), _coordinator.GetSnapshot().ReconnectRemaining);

            OperationResult cancel = _coordinator.CancelReconnect();
            Assert.AreEqual(MultiplayerErrorCode.Cancelled, cancel.ErrorCode);
            Assert.AreEqual(SessionPhase.Recovery, _coordinator.Phase);
        }

        [Test]
        public void HostLeftDuringReconnect_EndsInRecoveryImmediately()
        {
            ReachInGameGuest();
            _driver.Emit(new ConnectionEvent(
                ConnectionEventType.LocalDisconnected,
                TestData.Guest.Id,
                MultiplayerErrorCode.TransportFailed,
                "test.connection_lost"));

            _driver.Emit(new ConnectionEvent(
                ConnectionEventType.LocalDisconnected,
                TestData.Guest.Id,
                MultiplayerErrorCode.HostLeft,
                "test.host_left"));

            Assert.AreEqual(SessionPhase.Recovery, _coordinator.Phase);
            Assert.AreEqual(MultiplayerErrorCode.HostLeft, _coordinator.GetSnapshot().LastResult.ErrorCode);
        }

        [Test]
        public void ClientReconnect_RestoresSameSessionAndReturnsToInGame()
        {
            ReachInGameGuest();
            _driver.Emit(new ConnectionEvent(
                ConnectionEventType.LocalDisconnected,
                TestData.Guest.Id,
                MultiplayerErrorCode.TransportFailed,
                "test.connection_lost"));

            OperationResult result = Complete(
                _coordinator.ReconnectClientAsync(CancellationToken.None));

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(SessionPhase.InGame, _coordinator.Phase);
            Assert.AreEqual(2, _driver.StartClientCalls);
            Assert.AreEqual(2, _game.PrepareCalls);
            Assert.AreEqual(2, _game.LocalSynchronizeCalls);
            Assert.AreEqual(0, _coordinator.GetSnapshot().ReconnectAttempt);
            Assert.AreEqual(System.TimeSpan.Zero, _coordinator.GetSnapshot().ReconnectRemaining);
        }

        [Test]
        public void ClientReconnect_AcceptsStalePlatformDescriptorAfterAuthenticatedStart()
        {
            ReachReadyGuest();
            RoomSnapshot ready = _coordinator.CurrentRoom;
            Assert.IsTrue(_coordinator.ConfirmRemoteSessionStarted(
                ready.SessionId,
                ready.SessionGeneration + 1).Succeeded);
            Assert.Less(
                _platform.CurrentRoom.SessionGeneration,
                _coordinator.CurrentRoom.SessionGeneration);
            _driver.Emit(new ConnectionEvent(
                ConnectionEventType.LocalDisconnected,
                TestData.Guest.Id,
                MultiplayerErrorCode.TransportFailed,
                "test.connection_lost"));

            OperationResult result = Complete(
                _coordinator.ReconnectClientAsync(CancellationToken.None));

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(SessionPhase.InGame, _coordinator.Phase);
            Assert.AreEqual(
                ready.SessionGeneration + 1,
                _coordinator.CurrentRoom.SessionGeneration);
        }

        [Test]
        public void ClientReconnect_AttemptsExhaustedReportsReconnectExpired()
        {
            ReachInGameGuest();
            _driver.Emit(new ConnectionEvent(
                ConnectionEventType.LocalDisconnected,
                TestData.Guest.Id,
                MultiplayerErrorCode.TransportFailed,
                "test.connection_lost"));
            _driver.StartClientResult = OperationResult.Failure(
                MultiplayerErrorCode.TransportFailed,
                "test.reconnect_failed");

            OperationResult result = Complete(
                _coordinator.ReconnectClientAsync(CancellationToken.None));

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(MultiplayerErrorCode.ReconnectExpired, result.ErrorCode);
            Assert.AreEqual(SessionPhase.Recovery, _coordinator.Phase);
            Assert.AreEqual(
                MultiplayerErrorCode.ReconnectExpired,
                _coordinator.GetSnapshot().LastResult.ErrorCode);
            Assert.AreEqual(4, _driver.StartClientCalls);
        }

        [Test]
        public void HostTransportFailure_EntersRecoveryWithoutClientReconnect()
        {
            ReachInGameHost();

            _driver.Emit(new ConnectionEvent(
                ConnectionEventType.Failed,
                TestData.Host.Id,
                MultiplayerErrorCode.TransportFailed,
                "test.server_failed"));

            Assert.AreEqual(SessionPhase.Recovery, _coordinator.Phase);
            Assert.AreEqual(MultiplayerErrorCode.TransportFailed,
                _coordinator.GetSnapshot().LastResult.ErrorCode);
        }

        [Test]
        public void ClientAcceptsHostStartGenerationAdvance()
        {
            ReachReadyGuest();
            long generation = _coordinator.CurrentRoom.SessionGeneration + 1;
            RoomSnapshot starting = _coordinator.CurrentRoom.WithSession(
                generation,
                SessionPhase.Starting,
                false,
                false);
            _platform.EmitRoomEvent(new PlatformRoomEvent(
                RoomEventType.RoomUpdated,
                starting.Id,
                starting.SessionGeneration,
                starting,
                default(PlatformUserId)));
            Assert.AreEqual(SessionPhase.Starting, _coordinator.Phase);

            RoomSnapshot inGame = starting.WithSession(
                generation,
                SessionPhase.InGame,
                true,
                false);
            _platform.EmitRoomEvent(new PlatformRoomEvent(
                RoomEventType.RoomUpdated,
                inGame.Id,
                inGame.SessionGeneration,
                inGame,
                default(PlatformUserId)));

            Assert.AreEqual(SessionPhase.InGame, _coordinator.Phase);
            Assert.AreEqual(generation, _coordinator.CurrentRoom.SessionGeneration);
        }

        [Test]
        public void ClientAcceptsAuthenticatedGameStartWithoutPlatformRoomUpdate()
        {
            ReachReadyGuest();
            RoomSnapshot ready = _coordinator.CurrentRoom;
            long generation = ready.SessionGeneration + 1;

            OperationResult result = _coordinator.ConfirmRemoteSessionStarted(
                ready.SessionId,
                generation);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(SessionPhase.InGame, _coordinator.Phase);
            Assert.AreEqual(generation, _coordinator.CurrentRoom.SessionGeneration);
            Assert.IsTrue(_coordinator.CurrentRoom.HasStarted);
        }

        [Test]
        public void ClientRejectsAuthenticatedGameStartWithoutGenerationAdvance()
        {
            ReachReadyGuest();
            RoomSnapshot ready = _coordinator.CurrentRoom;

            OperationResult result = _coordinator.ConfirmRemoteSessionStarted(
                ready.SessionId,
                ready.SessionGeneration);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(MultiplayerErrorCode.SessionMismatch, result.ErrorCode);
            Assert.AreEqual(SessionPhase.Ready, _coordinator.Phase);
        }

        [Test]
        public void ClientAcceptsHostFailedGenerationAdvance()
        {
            ReachReadyGuest();
            long generation = _coordinator.CurrentRoom.SessionGeneration + 1;
            RoomSnapshot failed = _coordinator.CurrentRoom.WithSession(
                generation,
                SessionPhase.Failed,
                false,
                false);

            _platform.EmitRoomEvent(new PlatformRoomEvent(
                RoomEventType.RoomUpdated,
                failed.Id,
                failed.SessionGeneration,
                failed,
                default(PlatformUserId)));

            Assert.AreEqual(SessionPhase.Failed, _coordinator.Phase);
            Assert.AreEqual(
                MultiplayerErrorCode.SynchronizationFailed,
                _coordinator.GetSnapshot().LastResult.ErrorCode);
        }

        [Test]
        public void ServerPeerDisconnectAndExpiry_UseGameAdapterHooksWithoutChangingHostPhase()
        {
            ReachInGameHost();

            _driver.Emit(new ConnectionEvent(
                ConnectionEventType.PeerDisconnected,
                TestData.Guest.Id,
                MultiplayerErrorCode.TransportFailed,
                "test.peer_disconnected"));

            Assert.AreEqual(1, _game.PeerDisconnectedCalls);
            Assert.AreEqual(SessionPhase.InGame, _coordinator.Phase);
            Assert.AreEqual(
                MemberConnectionPhase.Disconnected,
                _coordinator.CurrentRoom.Members[1].ConnectionPhase);

            OperationResult expiry = _coordinator.ExpirePeerReconnect(TestData.GuestPeer());
            Assert.IsTrue(expiry.Succeeded);
            Assert.AreEqual(1, _game.ReconnectExpiredCalls);
            Assert.AreEqual(SessionPhase.InGame, _coordinator.Phase);
        }

        [Test]
        public void StaleRoomEvent_DoesNotReplaceCurrentSnapshot()
        {
            Assert.IsTrue(Complete(_coordinator.InitializeAsync(CancellationToken.None)).Succeeded);
            Assert.IsTrue(Complete(_coordinator.CreateRoomAsync(CancellationToken.None)).Succeeded);
            RoomSnapshot current = _coordinator.CurrentRoom;
            RoomSnapshot stale = TestData.CreateRoom(TestData.Host, sessionGeneration: current.SessionGeneration + 1);

            _platform.EmitRoomEvent(new PlatformRoomEvent(
                RoomEventType.RoomUpdated,
                current.Id,
                stale.SessionGeneration,
                stale,
                default(PlatformUserId)));

            Assert.AreSame(current, _coordinator.CurrentRoom);
        }

        [Test]
        public void PlatformMembershipRefresh_DoesNotRegressKnownReadyPhases()
        {
            Assert.IsTrue(Complete(_coordinator.InitializeAsync(CancellationToken.None)).Succeeded);
            Assert.IsTrue(Complete(_coordinator.CreateRoomAsync(CancellationToken.None)).Succeeded);
            RoomSnapshot refresh = TestData.CreateRoom(
                TestData.Host,
                MemberConnectionPhase.LobbyOnly,
                MemberConnectionPhase.Connected);

            _platform.EmitRoomEvent(new PlatformRoomEvent(
                RoomEventType.MemberJoined,
                refresh.Id,
                refresh.SessionGeneration,
                refresh,
                TestData.Guest.Id));
            OperationResult synchronized = Complete(_coordinator.SynchronizePeerAsync(
                TestData.GuestPeer(),
                CancellationToken.None));

            Assert.IsTrue(synchronized.Succeeded);
            Assert.AreEqual(SessionPhase.Ready, _coordinator.Phase);
            Assert.AreEqual(
                MemberConnectionPhase.Ready,
                _coordinator.CurrentRoom.Members[0].ConnectionPhase);
        }

        private void ReachReadyHost()
        {
            Assert.IsTrue(Complete(_coordinator.InitializeAsync(CancellationToken.None)).Succeeded);
            Assert.IsTrue(Complete(_coordinator.CreateRoomAsync(CancellationToken.None)).Succeeded);
            Assert.IsTrue(Complete(_coordinator.SynchronizePeerAsync(
                TestData.GuestPeer(),
                CancellationToken.None)).Succeeded);
            Assert.AreEqual(SessionPhase.Ready, _coordinator.Phase);
        }

        private void ReachInGameHost()
        {
            ReachReadyHost();
            Assert.IsTrue(Complete(_coordinator.StartGameAsync(CancellationToken.None)).Succeeded);
            Assert.AreEqual(SessionPhase.InGame, _coordinator.Phase);
        }

        private void ReachReadyGuest()
        {
            _platform.LocalUser = TestData.Guest;
            Assert.IsTrue(Complete(_coordinator.InitializeAsync(CancellationToken.None)).Succeeded);
            Assert.IsTrue(Complete(_coordinator.JoinRoomAsync(
                new RoomId("room-1"),
                CancellationToken.None)).Succeeded);
            Assert.AreEqual(SessionPhase.Ready, _coordinator.Phase);
        }

        private void ReachInGameGuest()
        {
            ReachReadyGuest();
            long generation = _coordinator.CurrentRoom.SessionGeneration + 1;
            RoomSnapshot inGame = _coordinator.CurrentRoom.WithSession(
                generation,
                SessionPhase.InGame,
                true,
                false);
            _platform.EmitRoomEvent(new PlatformRoomEvent(
                RoomEventType.RoomUpdated,
                inGame.Id,
                inGame.SessionGeneration,
                inGame,
                default(PlatformUserId)));
            Assert.AreEqual(SessionPhase.InGame, _coordinator.Phase);
        }

        private static T Complete<T>(Task<T> task)
        {
            return task.GetAwaiter().GetResult();
        }
    }
}
