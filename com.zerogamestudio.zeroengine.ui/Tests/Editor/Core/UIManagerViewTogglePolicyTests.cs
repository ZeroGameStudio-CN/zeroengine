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
    public sealed class UIManagerViewTogglePolicyTests
    {
        [Test]
        public void Resolve_OpenView_ClosesOnly()
        {
            var decision = UIManagerViewTogglePolicy.Resolve(isOpen: true);

            Assert.That(decision.CloseView, Is.True);
            Assert.That(decision.OpenView, Is.False);
        }

        [Test]
        public void Resolve_ClosedView_OpensOnly()
        {
            var decision = UIManagerViewTogglePolicy.Resolve(isOpen: false);

            Assert.That(decision.CloseView, Is.False);
            Assert.That(decision.OpenView, Is.True);
        }

        [Test]
        [Category("Boundary")]
        public void UIManager_ToggleView_DelegatesGenericToggleDecisionToPolicy()
        {
            var managerSource = File.ReadAllText(AssetPath("Runtime", "UI", "Core", "UIManager.cs"));
            var policyPath = AssetPath("Runtime", "UI", "Core", "Policies", "UIManagerViewTogglePolicy.cs");

            Assert.True(File.Exists(policyPath), "UIManager view toggle decisions must live outside UIManager.");

            var policySource = File.ReadAllText(policyPath);
            var toggleView = SourceTextRegionExtractor.ExtractMethodRegion(
                managerSource,
                "public void ToggleView(string viewName)");

            Assert.That(toggleView, Does.Contain("string.IsNullOrWhiteSpace(viewName)"));
            Assert.That(toggleView, Does.Contain("UIManagerViewTogglePolicy.Resolve("));
            Assert.That(toggleView, Does.Contain("IsOpen(viewName)"));
            Assert.That(toggleView, Does.Contain("Close(viewName);"));
            Assert.That(toggleView, Does.Contain("Open(viewName);"));
            Assert.That(toggleView, Does.Not.Contain("HardCodedViewName"));
            Assert.That(toggleView, Does.Not.Contain("if (IsOpen(viewName))"));
            AssertOrder(
                toggleView,
                "IsOpen(viewName)",
                "UIManagerViewTogglePolicy.Resolve(",
                "Toggle state must be queried before resolving the toggle decision.");
            AssertOrder(
                toggleView,
                "\n                Close(viewName);",
                "\n                Open(viewName);",
                "Close branch should remain the first toggle side-effect branch.");

            Assert.That(policySource, Does.Contain("public static UIManagerViewToggleDecision Resolve("));
            Assert.That(policySource, Does.Not.Contain("UnityEngine"));
            Assert.That(policySource, Does.Not.Contain("UIViewBase"));
            Assert.That(policySource, Does.Not.Contain("HardCodedViewName"));
            Assert.That(policySource, Does.Not.Contain("IsOpen"));
            Assert.That(policySource, Does.Not.Contain("Open("));
            Assert.That(policySource, Does.Not.Contain("Close("));
            Assert.That(policySource, Does.Not.Contain("_viewInstances"));
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

