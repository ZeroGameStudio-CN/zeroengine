using System.IO;
using NUnit.Framework;

namespace ZeroEngine.UI.Tests.Editor.Core
{
    [TestFixture]
    [Category("Boundary")]
    public sealed class UIViewBaseContractSourceTests
    {
        [Test]
        public void RuntimeCore_DoesNotReadLegacyInputOrProjectServices()
        {
            var packageRoot = Path.Combine(
                "Packages", "com.zerogamestudio.zeroengine.ui");
            var managerPath = Path.Combine(
                packageRoot, "Runtime", "UI", "Core", "UIManager.cs");
            var viewPath = Path.Combine(
                packageRoot, "Runtime", "UI", "Core", "UIViewBase.cs");

            var managerSource = File.ReadAllText(managerPath);
            var viewSource = File.ReadAllText(viewPath);

            StringAssert.DoesNotContain("UnityEngine." + "Input", managerSource);
            StringAssert.DoesNotContain("Input." + "Get", managerSource);
            StringAssert.DoesNotContain("Input" + "System", managerSource);
            StringAssert.DoesNotContain("Service" + "Registry", managerSource);
            StringAssert.DoesNotContain("GameTime" + "Manager", managerSource);
            StringAssert.DoesNotContain("Z" + "GS.", managerSource);
            StringAssert.DoesNotContain("Z" + "GS.", viewSource);
            StringAssert.Contains("IUIManagerHooks", managerSource);
            StringAssert.Contains("CancellationToken", viewSource);
            StringAssert.Contains("KillActiveAnimations", viewSource);
        }

        [Test]
        public void BeginTransition_CancelsDisposesAndReplacesTransitionSource()
        {
            var packageRoot = Path.Combine(
                "Packages", "com.zerogamestudio.zeroengine.ui");
            var viewPath = Path.Combine(
                packageRoot, "Runtime", "UI", "Core", "UIViewBase.cs");
            var viewSource = File.ReadAllText(viewPath);
            var beginTransition = SourceTextRegionExtractor.ExtractMethodRegion(
                viewSource,
                "private CancellationToken BeginTransition()");

            StringAssert.Contains("KillActiveAnimations();", beginTransition);
            StringAssert.Contains("_transitionCts.Dispose();", beginTransition);
            StringAssert.Contains("_transitionCts = new CancellationTokenSource();", beginTransition);
            Assert.That(
                beginTransition.IndexOf("KillActiveAnimations();", System.StringComparison.Ordinal),
                Is.LessThan(beginTransition.IndexOf("_transitionCts.Dispose();", System.StringComparison.Ordinal)));
            Assert.That(
                beginTransition.IndexOf("_transitionCts.Dispose();", System.StringComparison.Ordinal),
                Is.LessThan(beginTransition.IndexOf("_transitionCts = new CancellationTokenSource();", System.StringComparison.Ordinal)));
        }
    }
}
