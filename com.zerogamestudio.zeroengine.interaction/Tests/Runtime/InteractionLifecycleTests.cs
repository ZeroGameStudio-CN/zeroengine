using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZeroEngine.Interaction.Core;
using ZeroEngine.Interaction.Unity;

namespace ZeroEngine.Interaction.Tests
{
    public sealed class InteractionLifecycleTests
    {
        private GameObject root;
        private InteractionWorldBehaviour world;
        private InteractionAgentBehaviour actor;
        private InteractionTargetBehaviour target;
        private sealed class Clock : IInteractionClock { public double Now { get; set; } }

        [SetUp] public void SetUp()
        {
            root = new GameObject("Interaction test scope"); world = root.AddComponent<InteractionWorldBehaviour>();
            var targetObject = new GameObject("Target"); targetObject.transform.SetParent(root.transform);
            target = targetObject.AddComponent<InteractionTargetBehaviour>(); target.Configure(world, "target", InteractionPolicy.Press);
            var actorObject = new GameObject("Actor"); actorObject.transform.SetParent(root.transform);
            actor = actorObject.AddComponent<InteractionAgentBehaviour>(); actor.Configure(world, "actor");
        }
        [TearDown] public void TearDown() { Object.DestroyImmediate(root); }

        [UnityTest] public IEnumerator UnconfiguredActionFailsClosed()
        {
            yield return null;
            InteractionResult result = default; actor.Runtime.Completed += value => result = value;
            Assert.That(actor.Press(), Is.False); Assert.That(result.Status, Is.EqualTo(InteractionStatus.Denied));
            Assert.That(result.Reason, Does.Contain("No interaction action"));
        }

        [UnityTest] public IEnumerator PooledTargetRejectsThePreviousActivation()
        {
            InteractionRequest pending = null; int mutations = 0; target.Requested += request => pending = request;
            yield return null; actor.Press(); var stale = pending; var oldLease = target.Registration;
            target.gameObject.SetActive(false); Assert.That(stale.IsCurrent, Is.False);
            target.gameObject.SetActive(true); actor.Release(); actor.Runtime.RefreshFocus(); actor.Press();
            Assert.That(target.Registration.Activation, Is.GreaterThan(oldLease.Activation));
            Assert.That(stale.TryCommit(() => mutations++), Is.False);
            Assert.That(pending.TryCommit(() => mutations++), Is.True); Assert.That(mutations, Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator WorldDisableCancelsAndReenableRebindsActiveComponents()
        {
            InteractionRequest pending = null; target.Requested += request => pending = request;
            yield return null; actor.Press(); var stale = pending;
            world.enabled = false; Assert.That(stale.IsCurrent, Is.False); Assert.That(actor.Runtime, Is.Null);
            world.enabled = true; yield return null;
            Assert.That(world.Runtime.Count, Is.EqualTo(1)); Assert.That(actor.Runtime, Is.Not.Null);
            actor.Press(); Assert.That(pending.IsCurrent, Is.True);
        }

        [UnityTest] public IEnumerator DisableActorCancelsOutstandingConfirmation()
        {
            InteractionRequest pending = null; target.Requested += request => pending = request;
            yield return null; actor.Press(); actor.enabled = false;
            Assert.That(pending.IsCurrent, Is.False); Assert.That(pending.TryCommit(), Is.False);
            actor.enabled = true; yield return null; Assert.That(actor.Press(), Is.True);
        }

        [UnityTest] public IEnumerator InjectedClockControlsHoldWithoutWritingGlobalTimeScale()
        {
            var clock = new Clock(); int mutations = 0;
            target.Configure(world, "target", new InteractionPolicy(InteractionMode.Hold, 1));
            target.Requested += request => request.TryCommit(() => mutations++);
            actor.Configure(world, "actor", 2, clock); yield return null; actor.Press();
            yield return null; Assert.That(mutations, Is.Zero);
            clock.Now = 1; yield return null; Assert.That(mutations, Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator TwoAgentsHaveIndependentTargetsAndNoSharedFocus()
        {
            target.Requested += request => request.TryCommit();
            var otherObject = new GameObject("Other target"); otherObject.transform.SetParent(root.transform);
            otherObject.transform.position = Vector3.right * 10;
            var other = otherObject.AddComponent<InteractionTargetBehaviour>(); other.Configure(world, "other", InteractionPolicy.Press);
            other.Requested += request => request.TryCommit();
            var secondObject = new GameObject("Second actor"); secondObject.transform.SetParent(root.transform);
            secondObject.transform.position = Vector3.right * 10;
            var second = secondObject.AddComponent<InteractionAgentBehaviour>(); second.Configure(world, "second");
            yield return null;
            Assert.That(actor.Runtime.Focus.Id, Is.EqualTo("target")); Assert.That(second.Runtime.Focus.Id, Is.EqualTo("other"));
            Assert.That(actor.Press(), Is.True); Assert.That(second.Press(), Is.True);
        }
    }
}
