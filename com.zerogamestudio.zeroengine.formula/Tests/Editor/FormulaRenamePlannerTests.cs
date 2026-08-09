using System;
using System.Linq;
using NUnit.Framework;
using ZeroEngine.Formula.Editor;

namespace ZeroEngine.Formula.Tests.Editor
{
    public sealed class FormulaRenamePlannerTests
    {
        [Test]
        public void CreateDryRun_BuildsNewPathAndIncludesReferences()
        {
            var references = new[]
            {
                new FormulaAssetReference("Assets/Data/Ability.asset", "guid-1", "guid"),
            };

            var plan = FormulaRenamePlanner.CreateDryRun(
                "Assets/Assets/_Data/Math/TestMath.asset",
                "PlayerStatPercent",
                "guid-1",
                references,
                addressablesSyncSupported: true);

            Assert.AreEqual("Assets/Assets/_Data/Math/TestMath.asset", plan.CurrentPath);
            Assert.AreEqual("Assets/Assets/_Data/Math/PlayerStatPercent.asset", plan.NewPath);
            Assert.AreEqual(1, plan.References.Count);
            Assert.IsTrue(plan.CanApply);
        }

        [Test]
        public void CreateDryRun_BlocksAddressablesReferencesWithoutSyncSupport()
        {
            var references = new[]
            {
                new FormulaAssetReference("Assets/AddressableAssetsData/AssetGroups/Math.asset", "guid-1", "addressables"),
            };

            var plan = FormulaRenamePlanner.CreateDryRun(
                "Assets/Assets/_Data/Math/TestMath.asset",
                "PlayerStatPercent",
                "guid-1",
                references,
                addressablesSyncSupported: false);

            Assert.IsFalse(plan.CanApply);
            Assert.That(plan.BlockingIssues.Single(), Does.Contain("Addressables"));
        }

        [Test]
        public void CreateDryRun_RejectsEmptyName()
        {
            var plan = FormulaRenamePlanner.CreateDryRun(
                "Assets/Assets/_Data/Math/TestMath.asset",
                " ",
                "guid-1",
                Array.Empty<FormulaAssetReference>(),
                addressablesSyncSupported: true);

            Assert.IsFalse(plan.CanApply);
            Assert.That(plan.BlockingIssues.Single(), Does.Contain("名称"));
        }
    }
}
