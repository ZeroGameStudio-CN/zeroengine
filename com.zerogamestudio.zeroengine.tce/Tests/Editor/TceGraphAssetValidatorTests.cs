using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.TCE.Editor;

namespace ZeroEngine.TCE.Tests.Editor
{
    [TestFixture]
    public sealed class TceGraphAssetValidatorTests
    {
        [Test]
        public void Validate_NullAsset_ReturnsGraphNullIssue()
        {
            var issues = TceGraphAssetValidator.Validate(null);

            Assert.That(issues.Any(issue => issue.Code == TceValidationCodes.NullGraph));
        }

        [Test]
        public void Validate_InvalidAsset_ReturnsRuntimeValidationIssues()
        {
            var asset = ScriptableObject.CreateInstance<TceGraphAsset>();

            var issues = TceGraphAssetValidator.Validate(asset);

            Assert.That(issues.Any(issue => issue.Code == TceValidationCodes.MissingTrigger));
            Assert.That(issues.Any(issue => issue.Code == TceValidationCodes.MissingEffect));
        }
    }
}
