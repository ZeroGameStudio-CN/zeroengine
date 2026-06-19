using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZeroEngine.Dlc;
using ZeroEngine.Dlc.Editor;

namespace ZeroEngine.Tests.Dlc
{
    public sealed class DlcConfigValidatorTests
    {
        [Test]
        public void Validate_ReportsBrokenContentPackCatalog()
        {
            var catalog = ContentPackCatalog.CreateInMemory(new[]
            {
                new ContentPackDefinition("dlc.story", false, null, "Story DLC"),
                new ContentPackDefinition("dlc.story", true, "steam.dlc.story", string.Empty)
            });

            try
            {
                catalog.name = "InvalidCatalog";

                var issues = DlcConfigValidator.Validate(new[] { catalog });

                AssertError(issues, "Paid or optional content packs must declare the required DLC ID.");
                AssertError(issues, "Duplicate content pack ID 'dlc.story'.");
                AssertError(issues, "Content pack must have a display name.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(catalog);
            }
        }

        private static void AssertError(IReadOnlyList<DlcValidationIssue> issues, string expectedMessage)
        {
            Assert.That(
                issues.Any(issue => issue.Severity == DlcValidationSeverity.Error && issue.Message == expectedMessage),
                Is.True,
                $"Expected validation error '{expectedMessage}', got:\n{string.Join("\n", issues.Select(issue => issue.ToString()))}");
        }
    }
}
