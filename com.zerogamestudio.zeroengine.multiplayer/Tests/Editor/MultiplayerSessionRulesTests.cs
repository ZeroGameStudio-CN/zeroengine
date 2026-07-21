using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.Multiplayer.Tests
{
    public sealed class MultiplayerSessionRulesTests
    {
        [Test]
        public void StateMachine_OnlyAllowsDocumentedTransitions()
        {
            HashSet<string> legal = new HashSet<string>
            {
                Key(SessionPhase.Offline, SessionPhase.Initializing),
                Key(SessionPhase.Initializing, SessionPhase.Idle),
                Key(SessionPhase.Initializing, SessionPhase.Failed),
                Key(SessionPhase.Idle, SessionPhase.CreatingRoom),
                Key(SessionPhase.Idle, SessionPhase.JoiningRoom),
                Key(SessionPhase.CreatingRoom, SessionPhase.Connecting),
                Key(SessionPhase.CreatingRoom, SessionPhase.Failed),
                Key(SessionPhase.JoiningRoom, SessionPhase.Connecting),
                Key(SessionPhase.JoiningRoom, SessionPhase.Failed),
                Key(SessionPhase.Connecting, SessionPhase.InRoom),
                Key(SessionPhase.Connecting, SessionPhase.Synchronizing),
                Key(SessionPhase.Connecting, SessionPhase.Recovery),
                Key(SessionPhase.Connecting, SessionPhase.Failed),
                Key(SessionPhase.InRoom, SessionPhase.Synchronizing),
                Key(SessionPhase.InRoom, SessionPhase.Recovery),
                Key(SessionPhase.InRoom, SessionPhase.Leaving),
                Key(SessionPhase.Synchronizing, SessionPhase.Ready),
                Key(SessionPhase.Synchronizing, SessionPhase.InRoom),
                Key(SessionPhase.Synchronizing, SessionPhase.InGame),
                Key(SessionPhase.Synchronizing, SessionPhase.Reconnecting),
                Key(SessionPhase.Synchronizing, SessionPhase.Recovery),
                Key(SessionPhase.Synchronizing, SessionPhase.Failed),
                Key(SessionPhase.Ready, SessionPhase.Starting),
                Key(SessionPhase.Ready, SessionPhase.InRoom),
                Key(SessionPhase.Ready, SessionPhase.InGame),
                Key(SessionPhase.Ready, SessionPhase.Recovery),
                Key(SessionPhase.Ready, SessionPhase.Failed),
                Key(SessionPhase.Ready, SessionPhase.Leaving),
                Key(SessionPhase.Starting, SessionPhase.InGame),
                Key(SessionPhase.Starting, SessionPhase.Ready),
                Key(SessionPhase.Starting, SessionPhase.Recovery),
                Key(SessionPhase.Starting, SessionPhase.Failed),
                Key(SessionPhase.InGame, SessionPhase.Reconnecting),
                Key(SessionPhase.InGame, SessionPhase.Recovery),
                Key(SessionPhase.InGame, SessionPhase.Leaving),
                Key(SessionPhase.Reconnecting, SessionPhase.Synchronizing),
                Key(SessionPhase.Reconnecting, SessionPhase.Recovery),
                Key(SessionPhase.Recovery, SessionPhase.Leaving),
                Key(SessionPhase.Failed, SessionPhase.CreatingRoom),
                Key(SessionPhase.Failed, SessionPhase.JoiningRoom),
                Key(SessionPhase.Failed, SessionPhase.Idle),
                Key(SessionPhase.Failed, SessionPhase.Leaving),
                Key(SessionPhase.Leaving, SessionPhase.Idle)
            };

            Array phases = Enum.GetValues(typeof(SessionPhase));
            foreach (SessionPhase source in phases)
            {
                foreach (SessionPhase target in phases)
                {
                    Assert.AreEqual(
                        legal.Contains(Key(source, target)),
                        MultiplayerSessionStateMachine.CanTransition(source, target),
                        source + " -> " + target);
                }
            }
        }

        [Test]
        public void StateMachine_InvalidTransitionPreservesPhase()
        {
            MultiplayerSessionStateMachine stateMachine = new MultiplayerSessionStateMachine(SessionPhase.Idle);

            OperationResult result = stateMachine.TryTransition(SessionPhase.InGame);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(MultiplayerErrorCode.InvalidState, result.ErrorCode);
            Assert.AreEqual(SessionPhase.Idle, stateMachine.Phase);
        }

        [Test]
        public void OperationGenerationGate_RejectsStaleCompletion()
        {
            OperationGenerationGate gate = new OperationGenerationGate();
            int first = gate.Begin();
            int second = gate.Begin();

            Assert.IsFalse(gate.IsCurrent(first));
            Assert.IsTrue(gate.IsCurrent(second));

            gate.Invalidate();
            Assert.IsFalse(gate.IsCurrent(second));
        }

        [TestCase(SessionPhase.Idle, InviteRouteAction.Present)]
        [TestCase(SessionPhase.InRoom, InviteRouteAction.ConfirmLeaveCurrentSession)]
        [TestCase(SessionPhase.Starting, InviteRouteAction.QueueUntilStable)]
        [TestCase(SessionPhase.InGame, InviteRouteAction.QueueUntilStable)]
        [TestCase(SessionPhase.Leaving, InviteRouteAction.QueueUntilStable)]
        public void InviteRouter_RoutesBySessionPhase(SessionPhase phase, InviteRouteAction expected)
        {
            InviteRouter router = new InviteRouter();
            JoinRequest request = new JoinRequest(new RoomId("invited-room"), TestData.Guest);

            InviteRouteAction action = router.Route(request, phase, null);

            Assert.AreEqual(expected, action);
            Assert.IsTrue(router.HasPending);
        }

        [Test]
        public void InviteRouter_DeduplicatesPendingRoom()
        {
            InviteRouter router = new InviteRouter();
            JoinRequest request = new JoinRequest(new RoomId("invited-room"), TestData.Guest);
            router.Route(request, SessionPhase.Idle, null);

            InviteRouteAction action = router.Route(request, SessionPhase.InGame, null);

            Assert.AreEqual(InviteRouteAction.IgnoreDuplicate, action);
        }

        [Test]
        public void ReconnectPolicy_UsesConfiguredBackoffAndTimeout()
        {
            ReconnectPolicy policy = CreateReconnectPolicy();

            ReconnectAttempt first;
            ReconnectAttempt third;
            Assert.AreEqual(
                ReconnectBlockReason.None,
                policy.Evaluate(0, TimeSpan.Zero, false, out first));
            Assert.AreEqual(
                ReconnectBlockReason.None,
                policy.Evaluate(2, TimeSpan.FromSeconds(10), false, out third));

            Assert.AreEqual(1, first.Number);
            Assert.AreEqual(TimeSpan.FromSeconds(0.5), first.Delay);
            Assert.AreEqual(TimeSpan.FromSeconds(4), first.Timeout);
            Assert.AreEqual(3, third.Number);
            Assert.AreEqual(TimeSpan.FromSeconds(3), third.Delay);
            Assert.AreEqual(TimeSpan.FromSeconds(4), third.Timeout);
        }

        [Test]
        public void ReconnectPolicy_StopsForCancelAttemptsDeadlineAndGrace()
        {
            ReconnectPolicy policy = CreateReconnectPolicy();
            ReconnectAttempt ignored;

            Assert.AreEqual(
                ReconnectBlockReason.Cancelled,
                policy.Evaluate(0, TimeSpan.Zero, true, out ignored));
            Assert.AreEqual(
                ReconnectBlockReason.AttemptsExhausted,
                policy.Evaluate(3, TimeSpan.Zero, false, out ignored));
            Assert.AreEqual(
                ReconnectBlockReason.HardDeadlineExceeded,
                policy.Evaluate(0, TimeSpan.FromSeconds(18), false, out ignored));
            Assert.AreEqual(
                ReconnectBlockReason.GraceExpired,
                policy.Evaluate(0, TimeSpan.FromSeconds(20), false, out ignored));
            Assert.AreEqual(TimeSpan.Zero, policy.GetRemainingGrace(TimeSpan.FromSeconds(21)));
        }

        [Test]
        public void ReconnectSeatRegistry_OnlyOriginalIdentityCanReclaimSeat()
        {
            SessionId sessionId = new SessionId("session");
            ReconnectSeatRegistry registry = new ReconnectSeatRegistry(sessionId, 7);
            PlatformUserId owner = new PlatformUserId("owner");
            Assert.IsTrue(registry.Register(1, owner).Succeeded);
            Assert.IsTrue(registry.MarkDisconnected(owner).Succeeded);

            OperationResult<int> impostor = registry.TryReclaim(
                1,
                new PlatformUserId("impostor"),
                sessionId,
                7);
            OperationResult<int> staleSession = registry.TryReclaim(1, owner, sessionId, 6);
            OperationResult<int> ownerResult = registry.TryReclaim(1, owner, sessionId, 7);
            OperationResult<int> duplicate = registry.TryReclaim(1, owner, sessionId, 7);

            Assert.AreEqual(MultiplayerErrorCode.UnauthorizedPeer, impostor.ErrorCode);
            Assert.AreEqual(MultiplayerErrorCode.SessionMismatch, staleSession.ErrorCode);
            Assert.IsTrue(ownerResult.Succeeded);
            Assert.AreEqual(1, ownerResult.Value);
            Assert.AreEqual(MultiplayerErrorCode.SeatClaimed, duplicate.ErrorCode);
        }

        [Test]
        public void DefaultConfig_IsInternallyConsistent()
        {
            MultiplayerSessionConfig config = ScriptableObject.CreateInstance<MultiplayerSessionConfig>();
            try
            {
                Assert.IsEmpty(config.ValidateConfiguration());
                Assert.DoesNotThrow(() => config.CreateReconnectPolicy());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void UnityConfig_AdditionalValidation_IsPreservedThroughSettingsInterface()
        {
            MultiplayerSessionConfig config = ScriptableObject.CreateInstance<MultiplayerSessionConfig>();
            try
            {
                typeof(MultiplayerSessionConfig)
                    .GetField(
                        "metadataPrefix",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic)
                    .SetValue(config, string.Empty);

                CollectionAssert.Contains(
                    MultiplayerSessionSettings.Validate(config),
                    "multiplayer.config.metadata_prefix_missing");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void PlainSettings_AreValidatedWithoutUnityConfiguration()
        {
            FakeSessionSettings settings = new FakeSessionSettings();

            Assert.IsEmpty(MultiplayerSessionSettings.Validate(settings));

            ReconnectPolicy policy = MultiplayerSessionSettings.CreateReconnectPolicy(settings);
            ReconnectAttempt first;
            Assert.AreEqual(
                ReconnectBlockReason.None,
                policy.Evaluate(0, TimeSpan.Zero, false, out first));
            Assert.AreEqual(TimeSpan.FromSeconds(0.5), first.Delay);
        }

        [Test]
        public void PlainSettings_InvalidPlayerCount_IsRejected()
        {
            FakeSessionSettings settings = new FakeSessionSettings
            {
                MaxPlayers = 0,
                MinimumPlayersToStart = 1
            };

            CollectionAssert.Contains(
                MultiplayerSessionSettings.Validate(settings),
                "multiplayer.config.max_players_invalid");
        }

        private static ReconnectPolicy CreateReconnectPolicy()
        {
            return new ReconnectPolicy(
                3,
                TimeSpan.FromSeconds(4),
                new[]
                {
                    TimeSpan.FromSeconds(0.5),
                    TimeSpan.FromSeconds(1.5),
                    TimeSpan.FromSeconds(3)
                },
                TimeSpan.FromSeconds(18),
                TimeSpan.FromSeconds(20));
        }

        private static string Key(SessionPhase source, SessionPhase target)
        {
            return source + ">" + target;
        }
    }
}
