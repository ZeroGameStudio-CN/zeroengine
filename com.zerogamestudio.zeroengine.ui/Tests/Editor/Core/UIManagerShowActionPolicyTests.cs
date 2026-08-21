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
    public sealed class UIManagerShowActionPolicyTests
    {
        [Test]
        public void Resolve_HideOthers_PausesVisibleLayerSiblings()
        {
            var decision = UIManagerShowActionPolicy.Resolve(UIShowMode.HideOthers);

            AssertDecision(
                decision,
                pauseVisibleLayerSiblings: true,
                pauseTopView: false);
        }

        [Test]
        public void Resolve_Stack_PausesTopView()
        {
            var decision = UIManagerShowActionPolicy.Resolve(UIShowMode.Stack);

            AssertDecision(
                decision,
                pauseVisibleLayerSiblings: false,
                pauseTopView: true);
        }

        [Test]
        public void Resolve_Normal_DoesNotApplyShowModeSideEffects()
        {
            var decision = UIManagerShowActionPolicy.Resolve(UIShowMode.Normal);

            AssertDecision(
                decision,
                pauseVisibleLayerSiblings: false,
                pauseTopView: false);
        }

        [Test]
        public void Resolve_Singleton_DoesNotApplyShowModeSideEffects()
        {
            var decision = UIManagerShowActionPolicy.Resolve(UIShowMode.Singleton);

            AssertDecision(
                decision,
                pauseVisibleLayerSiblings: false,
                pauseTopView: false);
        }

        [Test]
        public void Resolve_UnknownShowMode_DoesNotApplyShowModeSideEffects()
        {
            var decision = UIManagerShowActionPolicy.Resolve((UIShowMode)999);

            AssertDecision(
                decision,
                pauseVisibleLayerSiblings: false,
                pauseTopView: false);
        }

        [Test]
        [Category("Boundary")]
        public void UIManager_HandleShowMode_DelegatesShowDecisionToPolicy()
        {
            var managerSource = File.ReadAllText(AssetPath("Runtime", "UI", "Core", "UIManager.cs"));
            var policyPath = AssetPath("Runtime", "UI", "Core", "Policies", "UIManagerShowActionPolicy.cs");

            Assert.True(File.Exists(policyPath), "UIManager show-mode decisions must live outside UIManager.");

            var policySource = File.ReadAllText(policyPath);
            var openAsync = SourceTextRegionExtractor.ExtractMethodRegion(
                managerSource,
                "private async Task<UIViewBase> OpenAsync(");
            var handleShowMode = SourceTextRegionExtractor.ExtractMethodRegion(
                managerSource,
                "private async Task HandleShowMode(UIViewBase view, UIViewConfig config)");

            AssertOrder(
                openAsync,
                "await HandleShowMode(view, config);",
                "_layerStacks[config.layer].Push(view);",
                "HandleShowMode must run before pushing the opened view onto the layer stack.");
            Assert.That(handleShowMode, Does.Contain("UIManagerShowActionPolicy.Resolve(config.showMode)"));
            Assert.That(handleShowMode, Does.Not.Contain("case UIShowMode.HideOthers"));
            Assert.That(handleShowMode, Does.Not.Contain("case UIShowMode.Stack"));

            Assert.That(policySource, Does.Contain("public static UIManagerShowActionDecision Resolve("));
            Assert.That(policySource, Does.Not.Contain("UnityEngine"));
            Assert.That(policySource, Does.Not.Contain("UIViewBase"));
            Assert.That(policySource, Does.Not.Contain("_layerStacks"));
            Assert.That(policySource, Does.Not.Contain("Stack<"));
            Assert.That(policySource, Does.Not.Contain("InternalPause"));
            Assert.That(policySource, Does.Not.Contain("IsVisible"));
        }

        private static void AssertDecision(
            UIManagerShowActionDecision decision,
            bool pauseVisibleLayerSiblings,
            bool pauseTopView)
        {
            Assert.That(decision.PauseVisibleLayerSiblings, Is.EqualTo(pauseVisibleLayerSiblings));
            Assert.That(decision.PauseTopView, Is.EqualTo(pauseTopView));
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


