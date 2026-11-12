using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.UI;

namespace ZeroEngine.UI.Tests.Editor.Core
{
    [TestFixture]
    [Category("Unit")]
    [Category("UI")]
    public sealed class UIManagerCloseTopPolicyTests
    {
        [Test]
        public void Resolve_MissingTopView_DoesNotClose()
        {
            var decision = UIManagerCloseTopPolicy.Resolve(
                hasTopView: false,
                allowEscClose: true);

            Assert.That(decision.CloseTopView, Is.False);
        }

        [Test]
        public void Resolve_TopViewDisallowsEscClose_DoesNotClose()
        {
            var decision = UIManagerCloseTopPolicy.Resolve(
                hasTopView: true,
                allowEscClose: false);

            Assert.That(decision.CloseTopView, Is.False);
        }

        [Test]
        public void Resolve_TopViewAllowsEscClose_Closes()
        {
            var decision = UIManagerCloseTopPolicy.Resolve(
                hasTopView: true,
                allowEscClose: true);

            Assert.That(decision.CloseTopView, Is.True);
        }

        [Test]
        [Category("Boundary")]
        public void UIManager_CloseTop_DelegatesCloseEligibilityToPolicy()
        {
            var managerSource = File.ReadAllText(AssetPath("Runtime", "UI", "Core", "UIManager.cs"));
            var policyPath = AssetPath("Runtime", "UI", "Core", "Policies", "UIManagerCloseTopPolicy.cs");

            Assert.True(File.Exists(policyPath), "UIManager close-top decisions must live outside UIManager.");

            var policySource = File.ReadAllText(policyPath);
            var closeTop = SourceTextRegionExtractor.ExtractMethodRegion(
                managerSource,
                "public void CloseTop()");

            Assert.That(closeTop, Does.Contain("var topView = _topView;"));
            Assert.That(closeTop, Does.Contain("UIManagerCloseTopPolicy.Resolve("));
            Assert.That(closeTop, Does.Contain("hasTopView: topView != null"));
            Assert.That(closeTop, Does.Contain("allowEscClose: topView != null && topView.Config != null && topView.Config.allowESCClose"));
            Assert.That(closeTop, Does.Contain("Close(topView);"));
            Assert.That(closeTop, Does.Not.Contain("if (_topView != null && _topView.Config.allowESCClose)"));
            AssertOrder(
                closeTop,
                "var topView = _topView;",
                "UIManagerCloseTopPolicy.Resolve(",
                "CloseTop must capture the top view before resolving the decision.");
            AssertOrder(
                closeTop,
                "UIManagerCloseTopPolicy.Resolve(",
                "Close(topView);",
                "CloseTop must resolve the close decision before calling Close.");

            Assert.That(policySource, Does.Contain("public static UIManagerCloseTopDecision Resolve("));
            Assert.That(policySource, Does.Not.Contain("UnityEngine"));
            Assert.That(policySource, Does.Not.Contain("UIViewBase"));
            Assert.That(policySource, Does.Not.Contain("_topView"));
            Assert.That(policySource, Does.Not.Contain("Config"));
            Assert.That(policySource, Does.Not.Contain("allowESCClose"));
            Assert.That(policySource, Does.Not.Contain("Close("));
        }

        private static void AssertOrder(string source, string first, string second, string message)
        {
            var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
            var secondIndex = source.IndexOf(second, StringComparison.Ordinal);

            Assert.That(firstIndex, Is.GreaterThanOrEqualTo(0), $"Missing first marker: {first}");
            Assert.That(secondIndex, Is.GreaterThanOrEqualTo(0), $"Missing second marker: {second}");
            Assert.That(firstIndex, Is.LessThan(secondIndex), message);
        }

        private static string AssetPath(params string[] parts)
        {
            var path = Path.Combine(parts);
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Packages", "com.zerogamestudio.zeroengine.ui", path);
        }
    }
}

