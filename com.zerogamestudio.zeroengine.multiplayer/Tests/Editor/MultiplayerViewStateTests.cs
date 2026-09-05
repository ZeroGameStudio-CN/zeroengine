using System;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Multiplayer.Presentation;

namespace ZeroEngine.Multiplayer.Tests
{
    public sealed class MultiplayerViewStateTests
    {
        private MultiplayerSessionConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<MultiplayerSessionConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_config);
        }

        [Test]
        public void Idle_AllowsCreateOnly()
        {
            MultiplayerViewState state = MultiplayerViewState.Create(
                Snapshot(SessionPhase.Idle, null, false, RetryOperationKind.None, OperationResult.Success()),
                _config);

            Assert.IsTrue(state.CanCreate);
            Assert.IsFalse(state.CanInvite);
            Assert.IsFalse(state.CanStart);
            Assert.IsFalse(state.CanLeave);
            Assert.IsFalse(state.CanRetry);
        }

        [Test]
        public void ReadyFullHostWithAllMembersReady_CanStartAndLeaveButCannotInvite()
        {
            RoomSnapshot room = TestData.CreateRoom(TestData.Host);
            MultiplayerViewState state = MultiplayerViewState.Create(
                Snapshot(SessionPhase.Ready, room, true, RetryOperationKind.None, OperationResult.Success()),
                _config);

            Assert.IsFalse(state.CanInvite);
            Assert.IsTrue(state.CanStart);
            Assert.IsTrue(state.CanLeave);
        }

        [Test]
        public void InRoomHostWithOpenSlot_CanInvite()
        {
            RoomSnapshot room = TestData.CreateRoom(TestData.Host, includeGuest: false);
            MultiplayerViewState state = MultiplayerViewState.Create(
                Snapshot(SessionPhase.InRoom, room, true, RetryOperationKind.None, OperationResult.Success()),
                _config);

            Assert.IsTrue(state.CanInvite);
            Assert.IsFalse(state.CanStart);
        }

        [Test]
        public void ReadyHostWithUnsynchronizedMember_CannotStart()
        {
            RoomSnapshot room = TestData.CreateRoom(
                TestData.Host,
                guestPhase: MemberConnectionPhase.Connected);

            MultiplayerViewState state = MultiplayerViewState.Create(
                Snapshot(SessionPhase.Ready, room, true, RetryOperationKind.None, OperationResult.Success()),
                _config);

            Assert.IsFalse(state.CanStart);
        }

        [Test]
        public void FailedCreate_ExposesRetryAndLocalizedErrorData()
        {
            OperationResult failure = OperationResult.Failure(
                MultiplayerErrorCode.CreateFailed,
                "test.create_failed",
                "detail");
            MultiplayerViewState state = MultiplayerViewState.Create(
                Snapshot(SessionPhase.Failed, null, false, RetryOperationKind.CreateRoom, failure),
                _config);

            Assert.IsTrue(state.CanRetry);
            Assert.IsTrue(state.CanLeave);
            Assert.AreEqual(MultiplayerErrorCode.CreateFailed, state.ErrorCode);
            Assert.AreEqual("test.create_failed", state.ErrorMessageKey);
            Assert.AreEqual("detail", state.ErrorArguments[0]);
        }

        private static MultiplayerSessionSnapshot Snapshot(
            SessionPhase phase,
            RoomSnapshot room,
            bool isServer,
            RetryOperationKind retryOperation,
            OperationResult result)
        {
            return new MultiplayerSessionSnapshot(
                phase,
                room,
                TestData.Host,
                isServer,
                false,
                retryOperation,
                result,
                TimeSpan.Zero);
        }
    }
}
