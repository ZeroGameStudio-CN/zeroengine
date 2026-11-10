using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Tutorial.Guidance;
using ZeroEngine.Tutorial.Presentation;

namespace ZeroEngine.Tutorial.Tests
{
    public sealed class TutorialContractTests
    {
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private TutorialManager manager;
        private double clock;

        [Serializable]
        public sealed class ProbeStep : TutorialStep
        {
            [NonSerialized] public int Entered, Exited, Skipped;
            [NonSerialized] public bool Done, ThrowEnter, ThrowUpdate;
            [NonSerialized] public Action EnterAction;
            public override string StepType => "Probe";
            public override void OnEnter(TutorialContext ctx) { Entered++; EnterAction?.Invoke(); if (ThrowEnter) throw new InvalidOperationException("synthetic enter failure"); }
            public override void OnUpdate(TutorialContext ctx) { if (ThrowUpdate) throw new InvalidOperationException("synthetic update failure"); }
            public override void OnExit(TutorialContext ctx) => Exited++;
            public override bool IsCompleted(TutorialContext ctx) => Done;
            public override void OnSkip(TutorialContext ctx) => Skipped++;
        }

        [SetUp] public void SetUp()
        {
            Assert.That(UnityEngine.Object.FindObjectOfType<TutorialManager>(), Is.Null, "Preserve an existing tutorial owner.");
            var root = Own(new GameObject("TutorialContractFixture")); manager = root.AddComponent<TutorialManager>();
            manager.Configure(null); manager.Context.Focus.Realtime = () => clock;
        }
        [TearDown] public void TearDown()
        {
            if (manager) manager.CancelCurrentSequence();
            foreach (var item in owned.AsEnumerable().Reverse()) if (item) UnityEngine.Object.DestroyImmediate(item);
            owned.Clear(); manager = null;
        }
        private T Own<T>(T value) where T : UnityEngine.Object { owned.Add(value); return value; }
        private TutorialSequenceSO Sequence(params TutorialStep[] steps)
        {
            var sequence = Own(ScriptableObject.CreateInstance<TutorialSequenceSO>());
            sequence.SequenceId = Guid.NewGuid().ToString("N"); sequence.AutoStart = false;
            sequence.Steps = steps.ToList(); manager.RegisterSequence(sequence); return sequence;
        }

        [Test] public void ScopedTargets_RejectDuplicateWritersAndPreserveReplacement()
        {
            var registry = new TutorialTargetRegistry(); var first = Own(new GameObject("First")); var second = Own(new GameObject("Second"));
            var registration = registry.Register("slot", first);
            Assert.Throws<InvalidOperationException>(() => registry.Register("slot", second));
            UnityEngine.Object.DestroyImmediate(first);
            var replacement = registry.Register("slot", second); registration.Dispose();
            Assert.That(registry.Find("slot"), Is.SameAs(second)); replacement.Dispose(); Assert.That(registry.Find("slot"), Is.Null);
        }

        [Test] public void PathResolution_RejectsAmbiguousNamesWithinTheScope()
        {
            var root = Own(new GameObject("Root"));
            new GameObject("Action").transform.SetParent(root.transform); new GameObject("Action").transform.SetParent(root.transform);
            var registry = new TutorialTargetRegistry();
            Assert.That(registry.Resolve(new TutorialTargetReference { Id = "action", Path = "Action" }, root.transform), Is.Null);
        }

        [Test] public void Signals_AreLocalAndUnsubscribedCallbacksNeverRun()
        {
            var first = new TutorialSignalBus(); var second = new TutorialSignalBus(); int count = 0;
            var lease = first.Subscribe((key, value) => count++);
            second.Publish("done"); Assert.That(count, Is.Zero); first.Publish("done"); Assert.That(count, Is.EqualTo(1));
            lease.Dispose(); first.Publish("done"); Assert.That(count, Is.EqualTo(1));
        }

        [Test] public void FocusValidation_RejectsAmbiguousOrUnfinishableRequests()
        {
            var request = new TutorialFocusRequest { Interaction = TutorialInteraction.Drag };
            request.Targets.Add(new TutorialTargetReference { Id = "same" }); request.Targets.Add(new TutorialTargetReference { Id = "same" });
            Assert.That(request.Validate().Count, Is.GreaterThan(0));
            request = new TutorialFocusRequest { Interaction = TutorialInteraction.Display };
            for (int i = 0; i < 16; i++) request.Targets.Add(new TutorialTargetReference { Id = "target-" + i });
            Assert.That(request.Validate().Count, Is.GreaterThan(0), "Dialogue must retain a reserved visual exclusion.");
            request = new TutorialFocusRequest { Interaction = TutorialInteraction.Signal };
            request.Targets.Add(new TutorialTargetReference { Id = "area", Kind = TutorialTargetKind.ScreenRegion, ScreenRegion = new Rect(-1, 0, 1, 1) });
            Assert.That(request.Validate().Count, Is.GreaterThan(0));
        }

        [Test] public void RuntimeRequests_CopyMutableConfiguration()
        {
            var source = new TutorialFocusRequest(); source.Targets.Add(new TutorialTargetReference { Id = "button" });
            var copy = source.RuntimeCopy(false); copy.Targets[0].Id = "runtime";
            Assert.That(source.CanSkip, Is.True); Assert.That(copy.CanSkip, Is.False); Assert.That(source.Targets[0].Id, Is.EqualTo("button"));
        }

        [Test] public void ShapeGeometry_DistinguishesRoundedCornersAndCircleBounds()
        {
            var rect = new Rect(-10, -10, 20, 20);
            Assert.That(TutorialTargetProjection.Contains(rect, TutorialMaskShape.Rectangle, 0, new Vector2(9, 9)), Is.True);
            Assert.That(TutorialTargetProjection.Contains(rect, TutorialMaskShape.RoundedRectangle, 8, new Vector2(9, 9)), Is.False);
            Assert.That(TutorialTargetProjection.Contains(rect, TutorialMaskShape.RoundedRectangle, 8, Vector2.zero), Is.True);
            Assert.That(TutorialTargetProjection.Contains(rect, TutorialMaskShape.Circle, 0, new Vector2(12, 0)), Is.True);
        }

        [Test] public void RequiredConditions_WithoutBindingsRemainUnsatisfied()
        {
            var context = manager.Context;
            Assert.That(new LevelCondition().IsSatisfied(context), Is.False);
            Assert.That(new QuestCondition { QuestId = "quest" }.IsSatisfied(context), Is.False);
            context.QueryState = key => key == "level" ? 3 : null;
            Assert.That(new LevelCondition { MinLevel = 2, MaxLevel = 4 }.IsSatisfied(context), Is.True);
            context.QueryQuestState = id => id == "quest" ? QuestRequiredState.Completed : (QuestRequiredState?)null;
            Assert.That(new QuestCondition { QuestId = "quest" }.IsSatisfied(context), Is.True);
        }

        [Test] public void EnterFailure_ExitsExactlyOnceWithoutCompletion()
        {
            var step = new ProbeStep { StepId = "enter", ThrowEnter = true }; var sequence = Sequence(step); int completed = 0;
            manager.OnStepCompleted += (_, __) => completed++;
            manager.StartSequence(sequence);
            Assert.That(manager.IsRunning, Is.False); Assert.That(step.Exited, Is.EqualTo(1)); Assert.That(completed, Is.Zero);
            Assert.That(manager.LastError, Does.Contain("enter failure"));
            manager.CancelCurrentSequence(); Assert.That(step.Exited, Is.EqualTo(1));
        }

        [Test] public void Skip_HasOneExitAndDoesNotEmitOperationCompletion()
        {
            var first = new ProbeStep { StepId = "first" }; var next = new ProbeStep { StepId = "next" };
            int completed = 0, skipped = 0; manager.OnStepCompleted += (_, __) => completed++; manager.OnStepSkipped += (_, __) => skipped++;
            manager.StartSequence(Sequence(first, next)); manager.SkipCurrentStep();
            Assert.That(first.Skipped, Is.EqualTo(1)); Assert.That(first.Exited, Is.EqualTo(1)); Assert.That(completed, Is.Zero);
            Assert.That(skipped, Is.EqualTo(1)); Assert.That(manager.CurrentStep, Is.SameAs(next));
        }

        [Test] public void UpdateFailure_StopsAndKeepsTheCheckpoint()
        {
            var step = new ProbeStep { StepId = "operation", ThrowUpdate = true }; var sequence = Sequence(step);
            manager.StartSequence(sequence); manager.Tick();
            Assert.That(step.Exited, Is.EqualTo(1)); Assert.That(manager.IsSequenceCompleted(sequence.SequenceId), Is.False);
            var saved = (TutorialSaveData)manager.ExportSaveData(); Assert.That(saved.CurrentStepId, Is.EqualTo("operation"));
        }

        [Test] public void PausedTutorial_UsesTheInjectedRealtimeClock()
        {
            var step = new ProbeStep { StepId = "clock" }; clock = 100;
            manager.StartSequence(Sequence(step)); clock = 107.5;
            Assert.That(manager.Context.StepElapsedTime, Is.EqualTo(7.5f).Within(.001f));
        }

        [Test] public void DisabledManager_CancelsItsActiveStep()
        {
            var step = new ProbeStep { StepId = "active" }; manager.StartSequence(Sequence(step)); manager.Enabled = false;
            Assert.That(manager.IsRunning, Is.False); Assert.That(step.Exited, Is.EqualTo(1));
        }

        [Test] public void CompositeCancellation_ExitsAllEnteredChildrenOnce()
        {
            var first = new ProbeStep(); var second = new ProbeStep();
            var composite = new CompositeStep { StepId = "parallel", Mode = CompositeStep.ExecutionMode.Parallel, SubSteps = new List<TutorialStep> { first, second } };
            manager.StartSequence(Sequence(composite)); manager.CancelCurrentSequence();
            Assert.That(first.Exited, Is.EqualTo(1)); Assert.That(second.Exited, Is.EqualTo(1));
        }

        [Test] public void OperationTimeout_IsFailureRatherThanLearnedInput()
        {
            var wait = new WaitEventStep { StepId = "event", EventKey = "actual-operation", Timeout = 2 };
            var sequence = Sequence(wait); clock = 0; manager.StartSequence(sequence); clock = 3; manager.Tick();
            Assert.That(wait.IsCompleted(manager.Context), Is.False); Assert.That(manager.IsSequenceCompleted(sequence.SequenceId), Is.False);
            Assert.That(manager.LastError, Does.Contain("timed out"));
        }

        [Test] public void Checkpoint_ResolvesStableStepIdentityAfterReordering()
        {
            var first = new ProbeStep { StepId = "first", Done = true }; var second = new ProbeStep { StepId = "second" };
            var sequence = Sequence(first, second); manager.StartSequence(sequence); manager.Tick();
            var saved = manager.ExportSaveData(); manager.CancelCurrentSequence(); sequence.Steps.Reverse(); manager.ImportSaveData(saved);
            Assert.That(manager.CurrentStep, Is.SameAs(second)); Assert.That(manager.CurrentStepIndex, Is.Zero);
        }

        [Test] public void QueuedTriggers_DeduplicateRevalidateAndChoosePriority()
        {
            var busy = Sequence(new ProbeStep()); manager.StartSequence(busy);
            var low = Sequence(new ProbeStep()); low.Priority = 1; var high = Sequence(new ProbeStep()); high.Priority = 10;
            bool valid = true; var pending = manager.QueueSequence(low, () => valid); var duplicate = manager.QueueSequence(low);
            manager.QueueSequence(high); Assert.That(duplicate.State, Is.EqualTo(TutorialRequestState.Rejected));
            valid = false; manager.CancelCurrentSequence(); manager.Tick();
            Assert.That(pending.State, Is.EqualTo(TutorialRequestState.Cancelled)); Assert.That(manager.CurrentSequence, Is.SameAs(high));
        }

        [Test] public void PresentationValidation_RequiresPlayableMediaAndReplacementNotes()
        {
            Assert.That(new TutorialMedia { Id = "video", Kind = TutorialMediaKind.Video }.Validate(), Is.Not.Null);
            Assert.That(new TutorialMedia { Id = "placeholder", Text = "test", Placeholder = true }.Validate(), Is.Not.Null);
            Assert.That(new TutorialMedia { Id = "dialogue", Kind = TutorialMediaKind.Dialogue, Backend = "project", Conversation = "story" }.Validate(), Is.Null);
            Assert.That(new TutorialMedia { Id = "choice", Text = "Choose", Choices = new[] { "A" } }.Validate(), Is.Not.Null);
            Assert.That(new TutorialMedia { Id = "choice", Text = "Choose", DialogueLayout = TutorialDialogueLayout.Normal, Choices = new[] { "A" } }.Validate(), Is.Null);
        }

        private sealed class RejectedRewards : ITutorialRewardService
        {
            public bool TryGrant(string id, IReadOnlyList<TutorialReward> rewards, out string error)
            { error = "synthetic project rejection"; return false; }
        }
        [Test] public void RejectedRewards_DoNotRecordSequenceCompletion()
        {
            var sequence = Sequence(new ProbeStep { StepId = "last", Done = true });
            sequence.CompletionRewards.Add(new ItemTutorialReward { ItemId = "synthetic", Amount = 1 });
            manager.Context.Rewards = new RejectedRewards();
            manager.StartSequence(sequence); manager.Tick();
            Assert.That(manager.IsRunning, Is.False); Assert.That(manager.IsSequenceCompleted(sequence.SequenceId), Is.False);
            Assert.That(manager.LastError, Does.Contain("project rejection"));
        }

        [Test] public void TutorialRuntime_DoesNotReferenceProjectOrDialogueVendorAssemblies()
        {
            var names = typeof(TutorialFocusView).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToArray();
            Assert.That(names.Any(name => name.StartsWith("POB", StringComparison.Ordinal) || name.Contains("PixelCrushers") || name.Contains("Odin")), Is.False);
        }
    }
}
