using NUnit.Framework;

namespace ZeroEngine.Cinematic.Tests
{
    public sealed class CinematicPlaybackWatchdogTests
    {
        [Test]
        public void ElapsedPastMaxSeconds_ReturnsTimedOut()
        {
            var watchdog = new CinematicPlaybackWatchdog(new CinematicPlaybackTimeoutPolicy(0.1f));

            var result = watchdog.Evaluate(elapsedSeconds: 0.2f, isPlaying: true);

            Assert.AreEqual(CinematicPlayStatus.TimedOut, result.Status);
            Assert.That(result.Message, Does.Contain("timed out"));
        }

        [Test]
        public void MaxNotSet_UsesValidationDefault()
        {
            var policy = CinematicPlaybackTimeoutPolicy.CreateDefault();

            Assert.Greater(policy.MaxPlaybackSeconds, 0f);
        }

        [Test]
        public void TimeoutResult_RequestsAbortCleanup()
        {
            var watchdog = new CinematicPlaybackWatchdog(new CinematicPlaybackTimeoutPolicy(0.1f));

            var result = watchdog.Evaluate(elapsedSeconds: 0.2f, isPlaying: true);

            Assert.IsTrue(result.RequiresAbortCleanup);
        }
    }
}
