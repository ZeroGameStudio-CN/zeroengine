using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZGS.Analytics.Editor;
using Object = UnityEngine.Object;

namespace ZGS.Analytics.Editor.Tests
{
    public sealed class AnalyticsConfigValidatorTests
    {
        [Test]
        public void Validate_ReportsBrokenEnabledAnalyticsConfig()
        {
            var config = ScriptableObject.CreateInstance<ZGSAnalyticsConfig>();

            try
            {
                config.EnableAnalytics = true;
                config.zgsServerUrl = "not-a-url";

                var issues = AnalyticsConfigValidator.Validate(new[] { config });

                AssertError(issues, "Enabled analytics config must define an app ID.");
                AssertError(issues, "Analytics server URL must be an absolute HTTP or HTTPS URL.");
                AssertError(issues, "Enabled analytics config must define an authentication secret.");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        private static void AssertError(IReadOnlyList<AnalyticsValidationIssue> issues, string expectedMessage)
        {
            Assert.That(issues.Any(issue => issue.Severity == AnalyticsValidationSeverity.Error && issue.Message == expectedMessage), Is.True);
        }
    }
}
