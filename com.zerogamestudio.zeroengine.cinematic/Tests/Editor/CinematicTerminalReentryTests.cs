using NUnit.Framework;

namespace ZeroEngine.Cinematic.Tests
{
    public sealed class CinematicTerminalReentryTests
    {
        [TestCase("exit-cancel")]
        [TestCase("adapter-exit-cancel")]
        [TestCase("exit-replace")]
        [TestCase("command-cancel")]
        [TestCase("timeout")]
        [TestCase("normal")]
        [TestCase("exit-failure")]
        public void TerminalCallbacks_RunOnce_RejectNestedTakeover_AndAllowLaterReplay(string mode)
        {
            var observed = TerminalReentryScenario.Run(mode);
            Assert.That(observed.exits, Is.EqualTo(1), "Only the captured playback may release once.");
            Assert.That(observed.terminalCommands, Is.EqualTo(mode == "command-cancel" ? 1 : 0));
            Assert.That(observed.nested, Is.EqualTo((mode == "exit-replace"
                ? CinematicPlayStatus.AlreadyPlaying : CinematicPlayStatus.None).ToString()));
            Assert.That(observed.outer, Is.EqualTo((mode == "timeout" ? CinematicPlayStatus.TimedOut
                : mode == "exit-failure" ? CinematicPlayStatus.Failed : CinematicPlayStatus.Cancelled).ToString()));
            if (mode == "exit-failure") Assert.That(observed.message, Does.Contain("owned exit failure"));
            Assert.That(observed.replayStart, Is.EqualTo(CinematicPlayStatus.Started.ToString()));
            Assert.That(observed.replayEnd, Is.EqualTo(CinematicPlayStatus.Cancelled.ToString()));
        }
    }
}
