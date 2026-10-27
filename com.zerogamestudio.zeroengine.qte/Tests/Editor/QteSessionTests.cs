using NUnit.Framework;

namespace ZeroEngine.Qte.Tests
{
    public sealed class QteSessionTests
    {
        [Test]
        public void TimingWindow_UsesSuppliedInputAndHasOneTerminalResult()
        {
            var session = new QteSession();
            session.Begin(QteMode.TimingWindow, new QteSettings { Duration = 2, TargetCenter = .6f, TargetWidth = .2f });
            session.Advance(1.2f, false); session.Press();
            Assert.That(session.Succeeded, Is.True);
            var snapshot = session.Snapshot;
            session.Advance(100, true); session.Press(); session.Cancel();
            Assert.That(session.State, Is.EqualTo(QteState.Succeeded));
            Assert.That(session.Age, Is.EqualTo(snapshot.Age));
            session.Begin(QteMode.TimingWindow, new QteSettings()); session.Advance(3, false); session.Press();
            Assert.That(session.Failed, Is.True);
        }

        [Test]
        public void TimingWindow_MissesOutsideTheTarget()
        {
            var session = new QteSession(); session.Begin(QteMode.TimingWindow, new QteSettings()); session.Press();
            Assert.That(session.Failed, Is.True); Assert.That(session.Progress, Is.Zero);
        }

        [Test]
        public void HoldRelease_OverloadFailsAndReleasingThroughSurgesWins()
        {
            var settings = new QteSettings { Duration = 10, ProgressPerSecond = .35f, ForceMultiplier = 1.465f,
                CalmDuration = .7f, SurgeDuration = .74f };
            var held = new QteSession(); held.Begin(QteMode.HoldRelease, settings); held.Press();
            for (int i = 0; i < 600 && held.Active; i++) held.Advance(1f / 60, true);
            Assert.That(held.Failed, Is.True);
            var balanced = new QteSession(); balanced.Begin(QteMode.HoldRelease, settings); balanced.Press();
            bool sawWarning = false, sawSurge = false;
            for (int i = 0; i < 600 && balanced.Active; i++)
            {
                sawWarning |= balanced.Preparing;
                if (balanced.Surging) { Assert.That(sawWarning, Is.True); sawSurge = true; }
                balanced.Advance(1f / 60, !balanced.Surging);
            }
            Assert.That(sawSurge, Is.True); Assert.That(balanced.Succeeded, Is.True);
        }

        [Test]
        public void HoldRelease_RequiresAnExplicitPress()
        {
            var session = new QteSession(); session.Begin(QteMode.HoldRelease, new QteSettings { Duration = 10 });
            session.Advance(4, true); Assert.That(session.Failed, Is.True); Assert.That(session.Progress, Is.Zero);
        }

        [Test]
        public void InvalidDelta_FreezesTheSessionAndCancellationIsExplicit()
        {
            foreach (var mode in new[] { QteMode.TimingWindow, QteMode.HoldRelease })
            {
                var session = new QteSession(); session.Begin(mode, new QteSettings { Duration = 10 });
                if (mode == QteMode.HoldRelease) session.Press();
                session.Advance(.3f, true); var before = session.Snapshot;
                foreach (float delta in new[] { 0, -1, float.NaN, float.PositiveInfinity }) session.Advance(delta, true);
                Assert.That(session.Age, Is.EqualTo(before.Age)); Assert.That(session.Load, Is.EqualTo(before.Load));
                session.Cancel(); Assert.That(session.State, Is.EqualTo(QteState.Cancelled));
                session.Advance(1, true); session.Press(); Assert.That(session.State, Is.EqualTo(QteState.Cancelled));
                session.Reset(); Assert.That(session.State, Is.EqualTo(QteState.Ready));
                Assert.That(session.Age + session.Progress + session.Load, Is.Zero);
            }
        }

        [Test]
        public void SessionFreezesSettingsAndDisplaySnapshotsWithoutMutatingTheirOwner()
        {
            var settings = new QteSettings { Duration = 2, TargetCenter = .5f, TargetWidth = .2f };
            var session = new QteSession(); session.Begin(QteMode.TimingWindow, settings);
            var before = session.Snapshot;
            settings.Duration = 20; settings.TargetCenter = .9f;
            session.Advance(1, false); session.Press();
            Assert.That(session.Succeeded, Is.True); Assert.That(before.Age, Is.Zero);
            Assert.That(settings.Duration, Is.EqualTo(20)); Assert.That(settings.TargetCenter, Is.EqualTo(.9f));
        }

        [Test]
        public void BoundedSubstepsAgreeAcrossCallerFrameRates()
        {
            var a = new QteSession(); var b = new QteSession();
            var settings = new QteSettings { Duration = 10, CalmDuration = 3, ProgressPerSecond = .2f };
            a.Begin(QteMode.HoldRelease, settings); b.Begin(QteMode.HoldRelease, settings); a.Press(); b.Press();
            a.Advance(1, true); for (int i = 0; i < 100; i++) b.Advance(.01f, true);
            Assert.That(a.Progress, Is.EqualTo(b.Progress).Within(.0001f));
            Assert.That(a.Load, Is.EqualTo(b.Load).Within(.0001f));
            Assert.That(a.State, Is.EqualTo(b.State));
        }
    }
}
