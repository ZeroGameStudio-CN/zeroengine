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
    public sealed class UIManagerModalSideEffectPolicyTests
    {
        [Test]
        public void Resolve_NoMaskNoPause_DoesNothing()
        {
            var decision = UIManagerModalSideEffectPolicy.Resolve(
                showMask: false,
                pauseGame: false);

            AssertDecision(
                decision,
                showMask: false,
                pauseGame: false,
                hideMask: false,
                resumeGame: false);
        }

        [Test]
        public void Resolve_ShowMaskOnly_ShowsAndHidesMask()
        {
            var decision = UIManagerModalSideEffectPolicy.Resolve(
                showMask: true,
                pauseGame: false);

            AssertDecision(
                decision,
                showMask: true,
                pauseGame: false,
                hideMask: true,
                resumeGame: false);
        }

        [Test]
        public void Resolve_PauseGameOnly_PausesAndResumesGame()
        {
            var decision = UIManagerModalSideEffectPolicy.Resolve(
                showMask: false,
                pauseGame: true);

            AssertDecision(
                decision,
                showMask: false,
                pauseGame: true,
                hideMask: false,
                resumeGame: true);
        }

        [Test]
        public void Resolve_ShowMaskAndPauseGame_AppliesBothSideEffectPairs()
        {
            var decision = UIManagerModalSideEffectPolicy.Resolve(
                showMask: true,
                pauseGame: true);

            AssertDecision(
                decision,
                showMask: true,
                pauseGame: true,
                hideMask: true,
                resumeGame: true);
        }

        [Test]
        [Category("Boundary")]
        public void UIManager_OpenAndClose_DelegateModalSideEffectGatesToPolicy()
        {
            var managerSource = File.ReadAllText(AssetPath("Runtime", "UI", "Core", "UIManager.cs"));
            var policyPath = AssetPath("Runtime", "UI", "Core", "Policies", "UIManagerModalSideEffectPolicy.cs");

            Assert.True(File.Exists(policyPath), "UIManager modal side-effect gates must live outside UIManager.");

            var policySource = File.ReadAllText(policyPath);
            var openAsync = SourceTextRegionExtractor.ExtractMethodRegion(
                managerSource,
                "private async Task<UIViewBase> OpenAsync(");
            var closeAsync = SourceTextRegionExtractor.ExtractMethodRegion(
                managerSource,
                "private async Task CloseCoreAsync(UIViewBase view, UICloseArgs args)");

            Assert.That(openAsync, Does.Contain("UIManagerModalSideEffectPolicy.Resolve("));
            Assert.That(openAsync, Does.Contain("showMask: config.showMask"));
            Assert.That(openAsync, Does.Contain("pauseGame: config.pauseGame"));
            Assert.That(openAsync, Does.Contain("if (modalSideEffectDecision.ShowMask)"));
            Assert.That(openAsync, Does.Contain("ShowMask(config.layer, config.maskColor, config.maskClickClose ? () => Close(view) : null, view);"));
            Assert.That(openAsync, Does.Contain("if (modalSideEffectDecision.PauseGame)"));
            Assert.That(openAsync, Does.Not.Contain("if (config.showMask)"));
            Assert.That(openAsync, Does.Not.Contain("if (config.pauseGame)"));

            Assert.That(closeAsync, Does.Contain("UIManagerModalSideEffectPolicy.Resolve("));
            Assert.That(closeAsync, Does.Contain("showMask: config.showMask"));
            Assert.That(closeAsync, Does.Contain("pauseGame: config.pauseGame"));
            Assert.That(closeAsync, Does.Contain("if (modalSideEffectDecision.HideMask)"));
            Assert.That(closeAsync, Does.Contain("RefreshMask(config.layer);"));
            Assert.That(closeAsync, Does.Contain("if (modalSideEffectDecision.ResumeGame)"));
            Assert.That(closeAsync, Does.Not.Contain("if (config.showMask)"));
            Assert.That(closeAsync, Does.Not.Contain("if (config.pauseGame)"));

            AssertOrder(
                openAsync,
                "await HandleShowMode(view, config);",
                "UIManagerModalSideEffectPolicy.Resolve(",
                "Show-mode side effects must still run before modal side effects.");
            AssertOrder(
                openAsync,
                "ShowMask(config.layer, config.maskColor, config.maskClickClose ? () => Close(view) : null, view);",
                "RequestPause(true);",
                "Mask display should stay before game pause.");
            AssertOrder(
                openAsync,
                "RequestPause(true);",
                "await view.InternalOpenAsync(args);",
                "Game pause should stay before the view open animation.");
            AssertOrder(
                closeAsync,
                "RemoveFromStack(view, config.layer);",
                "UIManagerModalSideEffectPolicy.Resolve(",
                "Stack removal must still happen before modal close side effects.");
            AssertOrder(
                closeAsync,
                "RefreshMask(config.layer);",
                "RequestPause(false);",
                "Mask reconciliation should stay before game resume.");
            AssertOrder(
                closeAsync,
                "RequestPause(false);",
                "await HandleCloseMode(view, config);",
                "Game resume should stay before close-mode side effects.");

            Assert.That(policySource, Does.Contain("public static UIManagerModalSideEffectDecision Resolve("));
            Assert.That(policySource, Does.Not.Contain("UnityEngine"));
            Assert.That(policySource, Does.Not.Contain("UIViewBase"));
            Assert.That(policySource, Does.Not.Contain("UIViewConfig"));
            Assert.That(policySource, Does.Not.Contain("ShowMask("));
            Assert.That(policySource, Does.Not.Contain("HideMask("));
            Assert.That(policySource, Does.Not.Contain("?.Pause"));
            Assert.That(policySource, Does.Not.Contain("?.Resume"));
        }

        private static void AssertDecision(
            UIManagerModalSideEffectDecision decision,
            bool showMask,
            bool pauseGame,
            bool hideMask,
            bool resumeGame)
        {
            Assert.That(decision.ShowMask, Is.EqualTo(showMask));
            Assert.That(decision.PauseGame, Is.EqualTo(pauseGame));
            Assert.That(decision.HideMask, Is.EqualTo(hideMask));
            Assert.That(decision.ResumeGame, Is.EqualTo(resumeGame));
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

