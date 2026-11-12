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
    public sealed class UIManagerOpenRequestPolicyTests
    {
        [Test]
        public void Resolve_SingletonExistingVisible_ReturnsExistingVisibleSingleton()
        {
            var decision = UIManagerOpenRequestPolicy.Resolve(
                UIShowMode.Singleton,
                hasExistingInstance: true,
                existingInstanceVisible: true);

            Assert.That(decision.ReturnExistingVisibleSingleton, Is.True);
        }

        [Test]
        public void Resolve_SingletonExistingHidden_DoesNotReturnExistingSingleton()
        {
            var decision = UIManagerOpenRequestPolicy.Resolve(
                UIShowMode.Singleton,
                hasExistingInstance: true,
                existingInstanceVisible: false);

            Assert.That(decision.ReturnExistingVisibleSingleton, Is.False);
        }

        [Test]
        public void Resolve_SingletonAbsent_DoesNotReturnExistingSingleton()
        {
            var decision = UIManagerOpenRequestPolicy.Resolve(
                UIShowMode.Singleton,
                hasExistingInstance: false,
                existingInstanceVisible: true);

            Assert.That(decision.ReturnExistingVisibleSingleton, Is.False);
        }

        [TestCase(UIShowMode.Normal)]
        [TestCase(UIShowMode.HideOthers)]
        [TestCase(UIShowMode.Stack)]
        public void Resolve_NonSingletonModes_DoNotReturnExistingSingleton(UIShowMode showMode)
        {
            var decision = UIManagerOpenRequestPolicy.Resolve(
                showMode,
                hasExistingInstance: true,
                existingInstanceVisible: true);

            Assert.That(decision.ReturnExistingVisibleSingleton, Is.False);
        }

        [Test]
        public void Resolve_UnknownShowMode_DoesNotReturnExistingSingleton()
        {
            var decision = UIManagerOpenRequestPolicy.Resolve(
                (UIShowMode)999,
                hasExistingInstance: true,
                existingInstanceVisible: true);

            Assert.That(decision.ReturnExistingVisibleSingleton, Is.False);
        }

        [Test]
        [Category("Boundary")]
        public void UIManager_OpenAsync_DelegatesSingletonEarlyReturnDecisionToPolicy()
        {
            var managerSource = File.ReadAllText(AssetPath("Runtime", "UI", "Core", "UIManager.cs"));
            var policyPath = AssetPath("Runtime", "UI", "Core", "Policies", "UIManagerOpenRequestPolicy.cs");

            Assert.True(File.Exists(policyPath), "UIManager singleton open-request decisions must live outside UIManager.");

            var policySource = File.ReadAllText(policyPath);
            var openAsync = SourceTextRegionExtractor.ExtractMethodRegion(
                managerSource,
                "private async Task<UIViewBase> OpenAsync(");

            Assert.That(openAsync, Does.Contain("UIManagerOpenRequestPolicy.Resolve("));
            Assert.That(openAsync, Does.Contain("LogUIManager(UIManagerLogPolicy.SingletonViewAlreadyOpen(viewName))"));
            Assert.That(openAsync, Does.Not.Contain("config.showMode == UIShowMode.Singleton && _viewInstances.ContainsKey(viewName)"));
            AssertOrder(
                openAsync,
                "_viewInstances.TryGetValue(viewName, out var existing)",
                "existing.IsVisible",
                "existing.IsVisible must not be evaluated before the singleton/key guard.");
            AssertOrder(
                openAsync,
                "existing.IsVisible",
                "return existing;",
                "Visible singleton requests must still return the existing view.");

            Assert.That(policySource, Does.Contain("public static UIManagerOpenRequestDecision Resolve("));
            Assert.That(policySource, Does.Not.Contain("UnityEngine"));
            Assert.That(policySource, Does.Not.Contain("UIViewBase"));
            Assert.That(policySource, Does.Not.Contain("_viewInstances"));
            Assert.That(policySource, Does.Not.Contain("LogUIManager"));
            Assert.That(policySource, Does.Not.Contain("UIManagerLogPolicy"));
            Assert.That(policySource, Does.Not.Contain("IsVisible"));
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

