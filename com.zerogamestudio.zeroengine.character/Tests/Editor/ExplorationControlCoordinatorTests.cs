using NUnit.Framework;
using ZeroEngine.Character.Exploration;

namespace ZeroEngine.Character.Tests.Editor
{
    [TestFixture]
    public sealed class ExplorationControlCoordinatorTests
    {
        [Test]
        public void Acquire_NestedLocks_OnlyRestoresPlayerAfterFinalLeaseRelease()
        {
            var coordinator = new ExplorationControlCoordinator(ExplorationControlMode.Player);
            var dialogue = coordinator.Acquire(
                ExplorationControlMode.Paused,
                "dialogue",
                "conversation",
                100);
            var cinematic = coordinator.Acquire(
                ExplorationControlMode.Scripted,
                "cinematic",
                "opening",
                200);

            Assert.That(coordinator.EffectiveMode, Is.EqualTo(ExplorationControlMode.Scripted));
            Assert.That(coordinator.EffectiveAuthority, Is.EqualTo(ExplorationMovementAuthority.Scripted));
            Assert.That(coordinator.ActiveTokenCount, Is.EqualTo(2));

            cinematic.Dispose();
            Assert.That(coordinator.EffectiveMode, Is.EqualTo(ExplorationControlMode.Paused));

            dialogue.Dispose();
            Assert.That(coordinator.EffectiveMode, Is.EqualTo(ExplorationControlMode.Player));
            Assert.That(coordinator.EffectiveAuthority, Is.EqualTo(ExplorationMovementAuthority.Player));
            Assert.That(coordinator.ActiveTokenCount, Is.Zero);
        }

        [Test]
        public void Dispose_CalledTwice_IsIdempotent()
        {
            var coordinator = new ExplorationControlCoordinator(ExplorationControlMode.Player);
            var lease = coordinator.Acquire(
                ExplorationControlMode.Loading,
                "world",
                "cell readiness",
                300);

            lease.Dispose();
            lease.Dispose();

            Assert.That(coordinator.EffectiveMode, Is.EqualTo(ExplorationControlMode.Player));
            Assert.That(coordinator.ActiveTokenCount, Is.Zero);
        }

        [Test]
        public void CanAccept_OnlyEffectiveAuthorityCanWrite()
        {
            var coordinator = new ExplorationControlCoordinator(ExplorationControlMode.Player);
            using var lease = coordinator.Acquire(
                ExplorationControlMode.Follower,
                "party",
                "leader switched",
                100);

            Assert.That(coordinator.CanAccept(ExplorationMovementAuthority.Follower), Is.True);
            Assert.That(coordinator.CanAccept(ExplorationMovementAuthority.Player), Is.False);
        }
    }
}
