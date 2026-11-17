using System;
using NUnit.Framework;

namespace ZeroEngine.TCE.Tests
{
    public sealed class TceAbilityGrantLifecycleTests
    {
        private sealed class Receiver : ITceObjectLifetime
        {
            public object Identity => this;
            public object NativeObject => this;
            public long Activation { get; set; }
            public long World => 0;
            public bool IsValid => true;
            public bool IsActive => true;
        }
        private sealed class Lease : IDisposable
        {
            public int Disposals;
            public Action OnDispose;
            public void Dispose() { Disposals++; OnDispose?.Invoke(); }
        }
        [Test] public void ReentrantInstallation_DoesNotInstallASecondChild()
        {
            var receiver = new Receiver(); var registry = new TceAbilityGrantRegistry();
            var key = new TceAbilityGrantKey(new TceObjectRef(receiver), this, "slot");
            var lease = new Lease(); int nestedInstalls = 0;
            Assert.That(registry.TryGrant(key, this, () =>
            {
                Assert.That(registry.TryGrant(key, new object(), () => { nestedInstalls++; return new Lease(); }, out _), Is.False);
                return lease;
            }, out _), Is.True);
            Assert.That(nestedInstalls, Is.Zero); Assert.That(registry.Count, Is.EqualTo(1));
            registry.Clear(); Assert.That(lease.Disposals, Is.EqualTo(1));
        }
        [Test] public void ClearDuringInstallation_ReleasesTheReturnedChild()
        {
            var registry = new TceAbilityGrantRegistry(); var lease = new Lease();
            var key = new TceAbilityGrantKey(new TceObjectRef(new Receiver()), this, "");
            Assert.That(registry.TryGrant(key, this, () => { registry.Clear(); return lease; }, out _), Is.False);
            Assert.That(lease.Disposals, Is.EqualTo(1)); Assert.That(registry.Count, Is.Zero);
        }
        [Test] public void RevokeDuringInstallation_DoesNotLeaveAnOrphan()
        {
            var registry = new TceAbilityGrantRegistry(); var lease = new Lease();
            var key = new TceAbilityGrantKey(new TceObjectRef(new Receiver()), this, "");
            Assert.That(registry.TryGrant(key, this, () => { registry.Revoke(key, this); return lease; }, out _), Is.False);
            Assert.That(lease.Disposals, Is.EqualTo(1)); Assert.That(registry.Count, Is.Zero);
        }
        [Test] public void Clear_RejectsNewGrantsFromRetiringChildren()
        {
            var registry = new TceAbilityGrantRegistry(); var receiver = new Receiver();
            var first = new TceAbilityGrantKey(new TceObjectRef(receiver), this, "first");
            var second = new TceAbilityGrantKey(new TceObjectRef(receiver), this, "second");
            bool installed = true;
            registry.TryGrant(first, this, () => new Lease { OnDispose = () =>
                installed = registry.TryGrant(second, this, () => new Lease(), out _) }, out _);
            registry.Clear(); Assert.That(installed, Is.False); Assert.That(registry.Count, Is.Zero);
        }
        [Test] public void ThrowingInstaller_CanBeRetriedByANewAttempt()
        {
            var registry = new TceAbilityGrantRegistry();
            var key = new TceAbilityGrantKey(new TceObjectRef(new Receiver()), this, "");
            Assert.Throws<InvalidOperationException>(() => registry.TryGrant(key, this, () => throw new InvalidOperationException(), out _));
            Assert.That(registry.TryGrant(key, this, () => new Lease(), out _), Is.True);
            registry.Clear();
        }
        [Test] public void ThrowingDisposer_DoesNotPreventOtherChildrenFromReleasing()
        {
            var registry = new TceAbilityGrantRegistry(); var receiver = new Receiver(); var other = new Lease();
            registry.TryGrant(new TceAbilityGrantKey(new TceObjectRef(receiver), this, "bad"), this,
                () => new Lease { OnDispose = () => throw new InvalidOperationException() }, out _);
            registry.TryGrant(new TceAbilityGrantKey(new TceObjectRef(receiver), this, "other"), this, () => other, out _);
            Assert.Throws<AggregateException>(() => registry.Clear());
            Assert.That(other.Disposals, Is.EqualTo(1)); Assert.That(registry.Count, Is.Zero);
        }
    }
}
