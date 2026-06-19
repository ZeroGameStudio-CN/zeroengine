using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Network.Config;
using ZeroEngine.Network.Editor;
using Object = UnityEngine.Object;

namespace ZeroEngine.Network.Editor.Tests
{
    public sealed class NetworkConfigValidatorTests
    {
        [Test]
        public void Validate_ReportsBrokenServerConfig()
        {
            var config = ScriptableObject.CreateInstance<ServerConfig>();

            try
            {
                config.name = "InvalidServer";
                config.Environment = ServerEnvironment.Local;
                config.DefaultIP = string.Empty;
                config.DefaultPort = 0;
                config.MaxPlayers = 0;
                config.TargetFrameRate = 0;
                config.OptimizeForHeadless = true;
                config.HeadlessTargetFrameRate = 0;

                var issues = NetworkConfigValidator.Validate(new[] { config });

                AssertError(issues, "Local server config must define a default IP.");
                AssertError(issues, "Default port cannot be zero.");
                AssertError(issues, "Max players must be positive.");
                AssertError(issues, "Target frame rate must be positive.");
                AssertError(issues, "Headless target frame rate must be positive when headless optimization is enabled.");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        private static void AssertError(IReadOnlyList<NetworkValidationIssue> issues, string expectedMessage)
        {
            Assert.That(
                issues.Any(issue => issue.Severity == NetworkValidationSeverity.Error && issue.Message == expectedMessage),
                Is.True,
                $"Expected validation error '{expectedMessage}', got:\n{string.Join("\n", issues.Select(issue => issue.ToString()))}");
        }
    }
}
