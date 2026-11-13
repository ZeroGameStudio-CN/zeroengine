using NUnit.Framework;
using UnityEngine;
using ZeroEngine.AI.CombatTactics;
using ZeroEngine.AI.UtilityAI;

namespace ZeroEngine.AI.Tests
{
    public sealed class CombatTacticsTests
    {
        private static CombatTacticsContext Ready() => new CombatTacticsContext {
            Profile = new CombatTacticsProfile(true, true, true, true, 16, .06f, .8f, .4f),
            Now = 10, Delta = .02f, HasTarget = true, BasePrimary = true, CanDash = true,
            ChargeTimeout = 2, SupportsHoldAttack = true
        };

        [TestCase(-1)] [TestCase(1)]
        public void RejectingSpeculativeDashAlsoRejectsItsMovement(int move)
        {
            var model = new CombatTacticsModel(); var c = Ready(); model.Step(c);
            c.Now += .02f; c.BasePrimary = false; c.BaseDash = true; c.BaseMoveX = move;
            var d = model.Step(c);
            Assert.That(d.HasMove && d.MoveX == 0 && !d.Dash && !d.Primary, Is.True);
            c.Now += .02f; c.BaseDash = false; c.BaseApproaching = true;
            Assert.That(model.Step(c).HasMove, Is.False);
        }

        [TestCase(-1)] [TestCase(1)]
        public void RealWalkingAndDashDefenseOverrideHeldFire(int move)
        {
            var model = new CombatTacticsModel(); var c = Ready(); model.Step(c);
            c.Now += .02f; c.Danger = true; c.ImpactTime = .5f; c.WalkEscapeDirection = move;
            var d = model.Step(c); Assert.That(d.MoveX, Is.EqualTo(move)); Assert.That(d.Dash, Is.False);
            c.Now += .02f; c.WalkEscapeDirection = 0; c.ImpactTime = .05f; c.EscapeDirection = -move;
            d = model.Step(c); Assert.That(d.Dash, Is.True); Assert.That(d.MoveX, Is.EqualTo(-move));
        }

        [Test]
        public void HoldWeaponKeepsOnePressAndStopsOffenseDuringTargetDodge()
        {
            var model = new CombatTacticsModel(); var c = Ready();
            Assert.That(model.Step(c).PressPrimary, Is.True);
            c.Now += .02f; var held = model.Step(c);
            Assert.That(held.Primary && !held.PressPrimary && held.HasMove && held.MoveX == 0, Is.True);
            c.Now += .02f; c.TargetDodging = true; c.BaseApproaching = true;
            var wait = model.Step(c); Assert.That(wait.Primary, Is.False); Assert.That(wait.HasMove, Is.False);
        }

        [Test]
        public void CounterIsRequestedOnlyOncePerActualDodgeWindow()
        {
            var model = new CombatTacticsModel(); var c = Ready();
            c.InPerfectDodge = true; c.CounterWindowOpen = true; c.DodgeEntry = 9;
            Assert.That(model.Step(c).PressPrimary, Is.True);
            c.Now += .02f; Assert.That(model.Step(c).PressPrimary, Is.False);
            c.Now += .02f; c.DodgeEntry = 10; Assert.That(model.Step(c).PressPrimary, Is.True);
        }

        [Test]
        public void ActorModelsKeepSeparateCooldownsAndResetTheirOwnState()
        {
            var a = new CombatTacticsModel(); var b = new CombatTacticsModel(); var c = Ready();
            c.Danger = true; c.ImpactTime = .05f; c.EscapeDirection = 1;
            Assert.That(a.Step(c).Dash, Is.True); c.Now += .01f;
            Assert.That(a.Step(c).Dash, Is.False); Assert.That(b.Step(c).Dash, Is.True);
            a.Reset(); Assert.That(a.Step(c).Dash, Is.True);
        }

        [Test]
        public void CapabilitiesDoNotGrantAnUnavailableNativeDashOrCharge()
        {
            var model = new CombatTacticsModel(); var c = Ready();
            c.SupportsCharge = false; Assert.That(model.Step(c).Action, Is.EqualTo(CombatTacticsAction.HoldAttack));
            c.Now += .02f; c.Danger = true; c.ImpactTime = .05f; c.CanDash = false;
            Assert.That(model.Step(c).Dash, Is.False);
        }

        [Test]
        public void AnimationCueVocabularyIsSuppliedByTheHost()
        {
            var cues = new[] { new AnimationEvent { functionName = "CustomCue", intParameter = 17, time = .6f } };
            Assert.That(CombatTacticsModel.TryAnimationImpact(cues, 1, .2f, 2, false, "CustomCue", 17, out var t), Is.True);
            Assert.That(t, Is.EqualTo(.2f).Within(.0001f));
            Assert.That(CombatTacticsModel.TryAnimationImpact(cues, 1, .2f, 2, false, "OtherCue", 17, out _), Is.False);
        }
    }

    public sealed class ActorClockTests
    {
        private GameObject owner;
        [SetUp] public void Setup() => owner = new GameObject("AI clock test");
        [TearDown] public void Cleanup() => Object.DestroyImmediate(owner);

        private sealed class CompleteAction : UtilityAction
        {
            public CompleteAction() { _cooldown = 5; }
            protected override AIActionResult OnUpdate(AIContext context, float deltaTime) => AIActionResult.Success();
        }

        [Test]
        public void FreshActionCanRunAtActorTimeZeroAndCooldownUsesThatClock()
        {
            float now = 0; var context = new AIContext(owner) { TimeProvider = () => now };
            var action = new CompleteAction(); Assert.That(action.CalculateScore(context), Is.GreaterThan(0));
            action.Start(context); action.Update(context, .02f);
            Assert.That(action.IsOnCooldown, Is.True); Assert.That(action.RemainingCooldown, Is.EqualTo(5));
            now = 4; Assert.That(action.CalculateScore(context), Is.Zero);
            now = 5; Assert.That(action.CalculateScore(context), Is.GreaterThan(0));
        }

        [Test]
        public void DurationAndCooldownConsiderationFreezeWithActorTime()
        {
            float now = 100; var context = new AIContext(owner) { TimeProvider = () => now, ActionStartTime = 98, LastDecisionTime = 99 };
            Assert.That(context.ActionDuration, Is.EqualTo(2)); Assert.That(context.TimeSinceLastDecision, Is.EqualTo(1));
            context.Blackboard.Set("Used", 99f);
            var consideration = new CooldownConsideration("Used", 2);
            Assert.That(consideration.Evaluate(context), Is.EqualTo(.5f).Within(.0001f));
            Assert.That(consideration.Evaluate(context), Is.EqualTo(.5f).Within(.0001f));
            now = 101; Assert.That(consideration.Evaluate(context), Is.EqualTo(1));
        }

        [Test]
        public void UtilityBrainSchedulesReevaluationUsingActorTime()
        {
            float now = 0; var context = new AIContext(owner) { TimeProvider = () => now };
            var brain = owner.AddComponent<UtilityBrain>(); brain.Initialize(context); brain.IsActive = true;
            int evaluations = 0; brain.OnEvaluationComplete += (_, _) => evaluations++;
            brain.AddAction(new WaitForever()); brain.Tick(0); Assert.That(evaluations, Is.EqualTo(1));
            brain.Tick(0); Assert.That(evaluations, Is.EqualTo(1));
            now = 1; brain.Tick(1); Assert.That(evaluations, Is.EqualTo(2));
        }

        private sealed class WaitForever : UtilityAction
        {
            protected override AIActionResult OnUpdate(AIContext context, float deltaTime) => AIActionResult.Running();
        }
    }
}
