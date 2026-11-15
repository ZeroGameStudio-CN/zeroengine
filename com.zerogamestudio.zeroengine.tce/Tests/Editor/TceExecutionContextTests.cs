using System;
using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.TCE.Tests.Editor
{
    public sealed class TceExecutionContextTests
    {
        private sealed class Lifetime : ITceObjectLifetime
        {
            public object Identity => this;
            public object NativeObject => this;
            public long Activation { get; set; }
            public long World { get; set; }
            public bool IsValid { get; set; } = true;
            public bool IsActive { get; set; } = true;
        }
        private sealed class Clock : ITceClock { public float Now => 0; }
        private sealed class Lease : IDisposable { public int Releases; public void Dispose() => Releases++; }

        [Test]
        public void ObjectRef_RejectsRecycledActivationAndWorld()
        {
            var actor = new Lifetime();
            var captured = new TceObjectRef(actor);
            Assert.That(captured.TryResolve<Lifetime>(out _), Is.True);
            actor.Activation++;
            Assert.That(captured.TryResolve<Lifetime>(out _), Is.False);
            var next = new TceObjectRef(actor);
            actor.World++;
            Assert.That(next.IsCurrent, Is.False);
        }

        [Test]
        public void ParentAndChild_HaveIndependentValuesAndKeepEventFacts()
        {
            var actor = new TceObjectRef(new Lifetime());
            var installation = new TceInstallationContext(this, actor, actor, new Clock(), null, this);
            var first = new TceEventContext(actor, actor, actor, actor, position: new Vector3(3, 4, 0));
            var parent = new TceExecutionContext(installation, first);
            var childEvent = new TceEventContext(actor, actor, actor, actor, cause: first);
            var child = new TceExecutionContext(installation, childEvent, parentExecutionId: parent.Id);
            parent.Set("parent");
            child.Set("child");
            Assert.That(parent.TryTake<string>(out var value), Is.True);
            Assert.That(value, Is.EqualTo("parent"));
            Assert.That(child.Event.RootId, Is.EqualTo(first.Id));
            Assert.That(child.Event.CauseId, Is.EqualTo(first.Id));
            Assert.That(child.ParentExecutionId, Is.EqualTo(parent.Id));
            Assert.That(first.Position, Is.EqualTo(new Vector3(3, 4, 0)));
        }

        [Test]
        public void Grants_RevokeOnlyTheirSources_AndNeverTransferToPooledObject()
        {
            var actor = new Lifetime();
            var key = new TceAbilityGrantKey(new TceObjectRef(actor), this, "test");
            var registry = new TceAbilityGrantRegistry();
            var firstSource = new object();
            var secondSource = new object();
            var lease = new Lease();
            int installs = 0;
            IDisposable Install() { installs++; return lease; }
            Assert.That(registry.TryGrant(key, firstSource, Install, out _), Is.True);
            Assert.That(registry.TryGrant(key, secondSource, Install, out _), Is.True);
            Assert.That(installs, Is.EqualTo(1));
            registry.RevokeSource(firstSource);
            Assert.That(lease.Releases, Is.Zero);
            actor.Activation++;
            registry.RemoveInactive();
            Assert.That(lease.Releases, Is.EqualTo(1));
            registry.RevokeSource(secondSource);
            Assert.That(lease.Releases, Is.EqualTo(1));
            Assert.That(registry.TryGrant(key, firstSource, Install, out _), Is.False);
        }

        private TceExecutionContext Execution(Func<bool> current = null)
        {
            var actor = new TceObjectRef(new Lifetime());
            return new TceExecutionContext(new TceInstallationContext(this, actor, actor, new Clock(), null, this),
                new TceEventContext(actor, actor, actor, actor), current);
        }

        [Test]
        public void Commit_UpdatesAllStateBeforePublishingReentrantNotifications()
        {
            var execution = Execution();
            int first = 1, second = 0, observed = 0;
            execution.DeferCommitNotification(() => observed = first + second);
            second = 2;
            Assert.That(observed, Is.Zero);
            execution.Commit();
            Assert.That(observed, Is.EqualTo(3));
            Assert.That(execution.IsCommitted, Is.True);
        }

        [Test]
        public void RefusedAdmission_CompletesWithoutSpendingOrPublishing()
        {
            var execution = Execution();
            int remaining = 1, notifications = 0;
            Assert.That(execution.TryReserveResource(execution.Installation.Host, "charge", 1, () => remaining,
                (amount, defer) => { remaining -= amount; defer(() => notifications++); }), Is.True);
            execution.Complete();
            Assert.That(remaining, Is.EqualTo(1));
            Assert.That(notifications, Is.Zero);
            Assert.That(execution.IsCommitted, Is.False);
        }

        [Test]
        public void SameResource_CombinesReservationsBeforeAnyPayment()
        {
            var execution = Execution();
            int remaining = 1;
            Action<int, Action<Action>> consume = (amount, _) => remaining -= amount;
            Assert.That(execution.TryReserveResource(execution.Installation.Host, "charge", 1, () => remaining, consume), Is.True);
            Assert.That(execution.TryReserveResource(execution.Installation.Host, "charge", 1, () => remaining, consume), Is.False);
            execution.Complete();
            Assert.That(remaining, Is.EqualTo(1));
        }

        [Test]
        public void ChangedResource_RejectsAllPaymentsAndStateCommits()
        {
            var execution = Execution();
            int first = 1, second = 1, stateCommits = 0;
            Assert.That(execution.TryReserveResource(execution.Installation.Host, "first", 1, () => first,
                (amount, _) => first -= amount), Is.True);
            Assert.That(execution.TryReserveResource(execution.Installation.Host, "second", 1, () => second,
                (amount, _) => second -= amount), Is.True);
            second = 0;
            Assert.That(execution.TryCommit(() => stateCommits++), Is.False);
            Assert.That(first, Is.EqualTo(1));
            Assert.That(stateCommits, Is.Zero);
            Assert.That(execution.IsCommitted, Is.False);
            execution.Complete();
        }

        [Test]
        public void ResourcePaymentsAndState_AreCompleteBeforeLifecycleNotifications()
        {
            var execution = Execution();
            int first = 1, second = 2, state = 0, callbacks = 0;
            Assert.That(execution.TryReserveResource(execution.Installation.Host, "first", 1, () => first,
                (amount, defer) =>
                {
                    first -= amount;
                    defer(() =>
                    {
                        callbacks++;
                        Assert.That(first + second, Is.Zero);
                        Assert.That(state, Is.EqualTo(1));
                        Assert.That(execution.IsCommitted, Is.True);
                    });
                }), Is.True);
            Assert.That(execution.TryReserveResource(execution.Installation.Host, "second", 2, () => second,
                (amount, _) => second -= amount), Is.True);
            Assert.That(execution.TryCommit(() => state++), Is.True);
            Assert.That(execution.TryCommit(() => state++), Is.True);
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(state, Is.EqualTo(1));
        }

        [Test]
        public void RecycledResourceOwner_RejectsCommitWithoutPayment()
        {
            var execution = Execution();
            var resourceOwner = new Lifetime();
            int remaining = 1;
            Assert.That(execution.TryReserveResource(new TceObjectRef(resourceOwner), "charge", 1, () => remaining,
                (amount, _) => remaining -= amount), Is.True);
            resourceOwner.Activation++;
            Assert.That(execution.TryCommit(), Is.False);
            Assert.That(remaining, Is.EqualTo(1));
        }

        [Test]
        public void CanceledAndCompletedFrames_CannotPublishNewContributions()
        {
            bool current = true;
            var execution = Execution(() => current);
            execution.Set("held");
            current = false;
            Assert.Throws<InvalidOperationException>(() => execution.Set("late"));
            Assert.That(execution.TryTake<string>(out _), Is.False);
            execution.Complete();
            Assert.That(execution.TryGet<string>(out _), Is.False);
            Assert.That(execution.IsCurrent, Is.False);
        }

        [Test]
        public void Grant_ActivationInvalidatedDuringInstall_ReleasesRejectedLease()
        {
            var actor = new Lifetime();
            var registry = new TceAbilityGrantRegistry();
            var lease = new Lease();
            bool granted = registry.TryGrant(new TceAbilityGrantKey(new TceObjectRef(actor), this, ""), this,
                () => { actor.Activation++; return lease; }, out _);
            Assert.That(granted, Is.False);
            Assert.That(lease.Releases, Is.EqualTo(1));
            Assert.That(registry.Count, Is.Zero);
        }

        [Test]
        public void InactiveObjectIdentity_IsSeparateFromLivenessPolicy()
        {
            var actor = new Lifetime { IsActive = false };
            var reference = new TceObjectRef(actor);
            Assert.That(reference.IsCurrent, Is.True);
            Assert.That(reference.IsActive, Is.False);
            actor.IsValid = false;
            Assert.That(reference.IsCurrent, Is.False);
        }

        [Test]
        public void ThinArea_SweptOutsideToOutside_ReportsBothBoundaries()
        {
            Assert.That(TceAreaGeometry.Intersect(TceAreaShape.Box, new Vector2(-10, 0), new Vector2(10, 0),
                0f, new Vector2(.5f, 4f), out float enter, out float exit), Is.True);
            Assert.That(enter, Is.GreaterThan(0f).And.LessThan(exit));
            Assert.That(exit, Is.LessThan(1f));
            Assert.That(TceAreaGeometry.Intersect(TceAreaShape.Circle, new Vector2(-10, 5), new Vector2(10, 5),
                1f, Vector2.one, out _, out _), Is.False);
        }

        [Test]
        public void ContributionGroups_ApplyStrongestOnce_AndRejectLateOffers()
        {
            var batch = new TceContributionBatch<string, string>(StringComparer.Ordinal);
            batch.Offer("element", 10, "weak");
            batch.Offer("element", 20, "strong");
            batch.Offer("element", 15, "middle");
            string applied = null;
            int count = 0;
            batch.Complete(value => { applied = value; count++; });
            batch.Complete(_ => count++);
            Assert.That(applied, Is.EqualTo("strong"));
            Assert.That(count, Is.EqualTo(1));
            Assert.That(batch.Offer("element", 30, "late"), Is.False);
        }

        [Test]
        public void EffectScope_ReplacesOnlyOneRecipient_AndDisposesExactlyOnce()
        {
            var scope = new TceEffectScope();
            var first = new TceObjectRef(new Lifetime());
            var second = new TceObjectRef(new Lifetime());
            var old = new Lease(); var next = new Lease(); var other = new Lease();
            scope.Replace(first, () => old);
            scope.Replace(second, () => other);
            scope.Replace(first, () => next);
            Assert.That(old.Releases, Is.EqualTo(1));
            Assert.That(other.Releases, Is.Zero);
            scope.Release(first); scope.Dispose(); scope.Dispose();
            Assert.That(next.Releases, Is.EqualTo(1));
            Assert.That(other.Releases, Is.EqualTo(1));
        }
    }
}
