using NUnit.Framework;
using ZeroEngine.Multiplayer.Editor;

namespace ZeroEngine.Multiplayer.Tests
{
    public sealed class MultiplayerSetupValidatorTests
    {
        [Test]
        public void ValidateConfig_NullConfigReturnsActionableError()
        {
            var issues = MultiplayerSetupValidator.ValidateConfig(null);

            Assert.That(issues.Count, Is.EqualTo(1));
            Assert.That(issues[0].Code, Is.EqualTo("multiplayer.setup.config_missing"));
            Assert.That(issues[0].Severity, Is.EqualTo(MultiplayerSetupIssueSeverity.Error));
        }

    }
}
