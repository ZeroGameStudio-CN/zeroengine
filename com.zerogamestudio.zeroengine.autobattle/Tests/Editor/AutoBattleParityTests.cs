using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace ZeroEngine.AutoBattle.Tests
{
    [TestFixture]
    [Category("ZeroEngine.AutoBattle")]
    public sealed class AutoBattleParityTests
    {
        [Test]
        public void GridReachability_UsesOriginThenRightLeftUpDownBfsOrder()
        {
            var grid = new TacticalGrid(3, 3);
            var result = new List<TacticalGridPosition>(9);
            var scratch = new TacticalGridTraversalScratch();

            grid.CollectReachable(new TacticalGridPosition(1, 1), 2, result, scratch);

            Assert.That(
                result,
                Is.EqualTo(new[]
                {
                    new TacticalGridPosition(1, 1),
                    new TacticalGridPosition(2, 1),
                    new TacticalGridPosition(0, 1),
                    new TacticalGridPosition(1, 2),
                    new TacticalGridPosition(1, 0),
                    new TacticalGridPosition(2, 2),
                    new TacticalGridPosition(2, 0),
                    new TacticalGridPosition(0, 2),
                    new TacticalGridPosition(0, 0)
                }));
        }

        [Test]
        public void AttackAvailable_SelectsHighestScoredAction()
        {
            TestActor actor = CreateActor(0, new TacticalGridPosition(1, 0));
            TestActor target = CreateActor(10, new TacticalGridPosition(2, 0), isTarget: true, actionScore: 10f);
            var policy = new TestPolicy { ActionsEnabled = true };

            TacticalDecisionResult<TestActor, TestPayload> result = Decide(
                actor,
                new[] { target },
                policy,
                new TacticalGrid(3, 1));

            Assert.That(result.Status, Is.EqualTo(TacticalDecisionStatus.Selected));
            Assert.That(result.Candidate.HasAction, Is.True);
            Assert.That(result.Candidate.Target.StableOrder, Is.EqualTo(10));
            Assert.That(result.InvalidScoreCount, Is.Zero);
        }

        [Test]
        public void LethalPriority_SelectsLethalTargetBeforeHealthyTarget()
        {
            TestActor actor = CreateActor(0, new TacticalGridPosition(0, 0));
            TestActor healthy = CreateActor(10, new TacticalGridPosition(1, 0), isTarget: true, actionScore: 20f);
            TestActor lethal = CreateActor(20, new TacticalGridPosition(2, 0), isTarget: true, actionScore: 100f);
            var policy = new TestPolicy { ActionsEnabled = true };

            TacticalDecisionResult<TestActor, TestPayload> result = Decide(
                actor,
                new[] { healthy, lethal },
                policy,
                new TacticalGrid(3, 1));

            Assert.That(result.Candidate.Target.StableOrder, Is.EqualTo(20));
        }

        [Test]
        public void PositionalPriority_UsesPolicyScore()
        {
            TestActor actor = CreateActor(0, new TacticalGridPosition(0, 0));
            TestActor lower = CreateActor(10, new TacticalGridPosition(1, 0), isTarget: true, actionScore: 1f);
            TestActor positional = CreateActor(20, new TacticalGridPosition(2, 0), isTarget: true, actionScore: 2f);
            var policy = new TestPolicy { ActionsEnabled = true };

            TacticalDecisionResult<TestActor, TestPayload> result = Decide(
                actor,
                new[] { lower, positional },
                policy,
                new TacticalGrid(3, 1));

            Assert.That(result.Candidate.Target.StableOrder, Is.EqualTo(20));
        }

        [Test]
        public void ShieldPressure_SelectsShieldedTarget()
        {
            TestActor actor = CreateActor(0, new TacticalGridPosition(0, 0));
            TestActor unshielded = CreateActor(10, new TacticalGridPosition(1, 0), isTarget: true, actionScore: 4f);
            TestActor shielded = CreateActor(20, new TacticalGridPosition(2, 0), isTarget: true, actionScore: 8f);
            var policy = new TestPolicy { ActionsEnabled = true };

            TacticalDecisionResult<TestActor, TestPayload> result = Decide(
                actor,
                new[] { unshielded, shielded },
                policy,
                new TacticalGrid(3, 1));

            Assert.That(result.Candidate.Target.StableOrder, Is.EqualTo(20));
        }

        [Test]
        public void MovementOnly_UsesBestMovementScoreWhenNoActionExists()
        {
            TestActor actor = CreateActor(0, new TacticalGridPosition(0, 0));
            var policy = new TestPolicy { ActionsEnabled = false };

            TacticalDecisionResult<TestActor, TestPayload> result = Decide(
                actor,
                Array.Empty<TestActor>(),
                policy,
                new TacticalGrid(3, 1),
                moveBudget: 2);

            Assert.That(result.Status, Is.EqualTo(TacticalDecisionStatus.Selected));
            Assert.That(result.Candidate.HasAction, Is.False);
            Assert.That(result.Candidate.Destination, Is.EqualTo(new TacticalGridPosition(2, 0)));
        }

        [Test]
        public void ActorPermutation_DoesNotChangeDecision()
        {
            TestActor actor = CreateActor(0, new TacticalGridPosition(0, 0));
            TestActor first = CreateActor(10, new TacticalGridPosition(1, 0), isTarget: true, actionScore: 50f);
            TestActor second = CreateActor(20, new TacticalGridPosition(2, 0), isTarget: true, actionScore: 50f);
            TestActor third = CreateActor(30, new TacticalGridPosition(3, 0), isTarget: true, actionScore: 50f);
            TestActor[][] permutations =
            {
                new[] { first, second, third },
                new[] { first, third, second },
                new[] { second, first, third },
                new[] { second, third, first },
                new[] { third, first, second },
                new[] { third, second, first }
            };

            int selectedOrder = -1;
            for (int index = 0; index < permutations.Length; index++)
            {
                var policy = new TestPolicy { ActionsEnabled = true };
                TacticalDecisionResult<TestActor, TestPayload> result = Decide(
                    actor,
                    permutations[index],
                    policy,
                    new TacticalGrid(4, 1));

                if (index == 0)
                {
                    selectedOrder = result.Candidate.Target.StableOrder;
                }

                Assert.That(result.Candidate.Target.StableOrder, Is.EqualTo(selectedOrder));
                Assert.That(result.Candidate.Destination, Is.EqualTo(new TacticalGridPosition(0, 0)));
            }
        }

        [Test]
        public void InvalidScores_AreCountedAndRejected()
        {
            TestActor actor = CreateActor(0, new TacticalGridPosition(0, 0));
            TestActor invalid = CreateActor(10, new TacticalGridPosition(1, 0), isTarget: true, actionScore: float.NaN);
            TestActor valid = CreateActor(20, new TacticalGridPosition(2, 0), isTarget: true, actionScore: 1f);
            var policy = new TestPolicy { ActionsEnabled = true };

            TacticalDecisionResult<TestActor, TestPayload> result = Decide(
                actor,
                new[] { invalid, valid },
                policy,
                new TacticalGrid(3, 1));

            Assert.That(result.Status, Is.EqualTo(TacticalDecisionStatus.Selected));
            Assert.That(result.Candidate.Target.StableOrder, Is.EqualTo(20));
            Assert.That(result.InvalidScoreCount, Is.EqualTo(2));
        }

        [Test]
        public void DuplicateStableOrders_AreRejected()
        {
            TestActor actor = CreateActor(0, new TacticalGridPosition(0, 0));
            TestActor first = CreateActor(10, new TacticalGridPosition(1, 0), isTarget: true);
            TestActor duplicate = CreateActor(10, new TacticalGridPosition(2, 0), isTarget: true);
            var policy = new TestPolicy { ActionsEnabled = true };
            var scratch = new TacticalDecisionScratch<TestActor, TestPayload>();

            Assert.Throws<ArgumentException>(() => TacticalDecisionPlanner.Decide(
                new TacticalGrid(3, 1),
                in actor,
                new[] { first, duplicate },
                1,
                policy,
                scratch));
        }

        [Test]
        public void Planner_ReusesScratchAndClearsBuffers()
        {
            TestActor actor = CreateActor(0, new TacticalGridPosition(1, 1));
            TestActor target = CreateActor(10, new TacticalGridPosition(2, 1), isTarget: true, actionScore: 1f);
            TestActor[] actors = { target };
            TacticalGrid grid = new TacticalGrid(3, 3);
            var policy = new TestPolicy { ActionsEnabled = true };
            var scratch = new TacticalDecisionScratch<TestActor, TestPayload>();

            TacticalDecisionPlanner.Decide(grid, in actor, actors, 1, policy, scratch);
            List<TestActor> stableActors = GetField<List<TestActor>>(scratch, "StableActors");
            List<long> stableOrders = GetField<List<long>>(scratch, "StableActorOrders");
            List<TacticalGridPosition> reachable = GetField<List<TacticalGridPosition>>(scratch, "Reachable");
            TacticalGridTraversalScratch traversal = GetField<TacticalGridTraversalScratch>(scratch, "Traversal");
            TacticalGridPosition[] queue = GetProperty<TacticalGridPosition[]>(traversal, "Queue");
            int[] queueDistances = GetProperty<int[]>(traversal, "QueueDistances");
            bool[] visited = GetProperty<bool[]>(traversal, "Visited");

            Assert.That(stableActors, Has.Count.EqualTo(0));
            Assert.That(stableOrders, Has.Count.EqualTo(0));
            Assert.That(reachable, Has.Count.EqualTo(0));

            TacticalDecisionPlanner.Decide(grid, in actor, actors, 1, policy, scratch);

            Assert.That(GetProperty<TacticalGridPosition[]>(traversal, "Queue"), Is.SameAs(queue));
            Assert.That(GetProperty<int[]>(traversal, "QueueDistances"), Is.SameAs(queueDistances));
            Assert.That(GetProperty<bool[]>(traversal, "Visited"), Is.SameAs(visited));
            Assert.That(stableActors.Capacity, Is.GreaterThanOrEqualTo(actors.Length));
            Assert.That(stableOrders.Capacity, Is.GreaterThanOrEqualTo(actors.Length));
            Assert.That(reachable.Capacity, Is.GreaterThanOrEqualTo(grid.Width * grid.Height));
            Assert.That(stableActors, Has.Count.EqualTo(0));
            Assert.That(stableOrders, Has.Count.EqualTo(0));
            Assert.That(reachable, Has.Count.EqualTo(0));
        }

        [Test]
        public void Grid_OriginMayBeOccupiedAndStillStartsReachability()
        {
            var grid = new TacticalGrid(2, 1);
            var result = new List<TacticalGridPosition>();
            grid.SetOccupied(new TacticalGridPosition(0, 0), true);

            grid.CollectReachable(
                new TacticalGridPosition(0, 0),
                1,
                result,
                new TacticalGridTraversalScratch());

            Assert.That(result, Is.EqualTo(new[]
            {
                new TacticalGridPosition(0, 0),
                new TacticalGridPosition(1, 0)
            }));
        }

        private static TacticalDecisionResult<TestActor, TestPayload> Decide(
            TestActor actor,
            IReadOnlyList<TestActor> actors,
            TestPolicy policy,
            TacticalGrid grid,
            int moveBudget = 1)
        {
            var scratch = new TacticalDecisionScratch<TestActor, TestPayload>();
            return TacticalDecisionPlanner.Decide(grid, in actor, actors, moveBudget, policy, scratch);
        }

        private static TestActor CreateActor(
            int stableOrder,
            TacticalGridPosition position,
            bool isTarget = false,
            float actionScore = 0f)
        {
            return new TestActor
            {
                StableOrder = stableOrder,
                Position = position,
                IsTarget = isTarget,
                ActionScore = actionScore
            };
        }

        private static T GetField<T>(object instance, string name)
        {
            FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing scratch field '{name}'.");
            return (T)field.GetValue(instance);
        }

        private static T GetProperty<T>(object instance, string name)
        {
            PropertyInfo property = instance.GetType().GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null, $"Missing scratch property '{name}'.");
            return (T)property.GetValue(instance);
        }

        private struct TestActor
        {
            public int StableOrder;
            public TacticalGridPosition Position;
            public bool IsTarget;
            public float ActionScore;
        }

        private struct TestPayload
        {
            public int TargetStableOrder;
            public TacticalGridPosition Destination;
        }

        private sealed class TestPolicy : ITacticalDecisionPolicy<TestActor, TestPayload>
        {
            public bool ActionsEnabled { get; set; }

            public TacticalGridPosition GetPosition(in TestActor actor)
            {
                return actor.Position;
            }

            public long GetStableActorOrder(in TestActor actor)
            {
                return actor.StableOrder;
            }

            public bool IsTargetValid(in TestActor actor, in TestActor target)
            {
                return actor.StableOrder != target.StableOrder && target.IsTarget;
            }

            public bool TryEvaluateAction(
                in TestActor actor,
                TacticalGridPosition destination,
                in TestActor target,
                out TestPayload payload,
                out float score)
            {
                payload = new TestPayload
                {
                    TargetStableOrder = target.StableOrder,
                    Destination = destination
                };
                score = target.ActionScore;
                return ActionsEnabled && target.IsTarget;
            }

            public float ScoreMovement(
                in TestActor actor,
                TacticalGridPosition destination,
                IReadOnlyList<TestActor> stableTargets)
            {
                return destination.X;
            }
        }
    }
}
