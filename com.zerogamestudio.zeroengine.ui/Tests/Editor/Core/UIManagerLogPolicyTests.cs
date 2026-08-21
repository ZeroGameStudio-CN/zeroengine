using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.UI;

namespace ZeroEngine.UI.Tests.Editor.Core
{
    [TestFixture]
    [Category("Unit")]
    [Category("UI")]
    public sealed class UIManagerLogPolicyTests
    {
        [Test]
        public void ViewRequestDecisions_ReturnExpectedLevelsAndPayloads()
        {
            AssertDecision(UIManagerLogPolicy.ViewNameEmpty(), UIManagerLogLevel.Error, "View name is empty!");
            AssertDecision(
                UIManagerLogPolicy.ViewAlreadyRegistered("InventoryView"),
                UIManagerLogLevel.Warning,
                "View 'InventoryView' already registered, overwriting...");
            AssertDecision(
                UIManagerLogPolicy.ViewConfigNotFound("MissingView"),
                UIManagerLogLevel.Error,
                "View config not found: MissingView");
            AssertDecision(
                UIManagerLogPolicy.SingletonViewAlreadyOpen("PauseView"),
                UIManagerLogLevel.Warning,
                "Singleton view 'PauseView' is already open");
            AssertDecision(
                UIManagerLogPolicy.ViewNotFound("GhostView"),
                UIManagerLogLevel.Warning,
                "View not found: GhostView");
            AssertDecision(
                UIManagerLogPolicy.ViewPrefabLoadFailed("MainView"),
                UIManagerLogLevel.Error,
                "Failed to load view prefab for: MainView");
            AssertDecision(
                UIManagerLogPolicy.ViewComponentNotFound("MainView"),
                UIManagerLogLevel.Error,
                "View component not found on: MainView");
            AssertDecision(
                UIManagerLogPolicy.AddressablesLoadFailed("UI/MainView", "load failed"),
                UIManagerLogLevel.Error,
                "Addressables load failed for: UI/MainView, load failed");
            AssertDecision(
                UIManagerLogPolicy.AddressablesLoadFailed("UI/MainView"),
                UIManagerLogLevel.Error,
                "Addressables load failed for: UI/MainView");
        }

        [Test]
        public void DefaultDecision_DoesNotLog()
        {
            var decision = default(UIManagerLogDecision);

            Assert.That(decision.ShouldLog, Is.False);
            Assert.That(decision.Level, Is.EqualTo(UIManagerLogLevel.None));
            Assert.That(decision.Message, Is.EqualTo(string.Empty));
        }

        [Test]
        public void UIManagerHooks_DispatchesPauseAndLogToInjectedDelegates()
        {
            var pauseValue = false;
            var logLevel = UIManagerLogLevel.None;
            var logMessage = string.Empty;
            var preparedResourceKey = string.Empty;
            var recordedResourceKey = string.Empty;
            var recordedDuration = TimeSpan.Zero;
            var recordedSucceeded = false;
            var hooks = new UIManagerHooks(
                pause: paused => pauseValue = paused,
                log: (level, message) =>
                {
                    logLevel = level;
                    logMessage = message;
                },
                preparePrefabLoad: resourceKey =>
                {
                    preparedResourceKey = resourceKey;
                    return Task.CompletedTask;
                },
                recordPrefabLoad: (resourceKey, duration, succeeded) =>
                {
                    recordedResourceKey = resourceKey;
                    recordedDuration = duration;
                    recordedSucceeded = succeeded;
                });

            hooks.RequestPause(true);
            hooks.Log(UIManagerLogLevel.Warning, "hook message");
            hooks.PreparePrefabLoadAsync("ui/main-menu").GetAwaiter().GetResult();
            hooks.RecordPrefabLoad("ui/main-menu", TimeSpan.FromMilliseconds(25), true);

            Assert.That(pauseValue, Is.True);
            Assert.That(logLevel, Is.EqualTo(UIManagerLogLevel.Warning));
            Assert.That(logMessage, Is.EqualTo("hook message"));
            Assert.That(preparedResourceKey, Is.EqualTo("ui/main-menu"));
            Assert.That(recordedResourceKey, Is.EqualTo("ui/main-menu"));
            Assert.That(recordedDuration, Is.EqualTo(TimeSpan.FromMilliseconds(25)));
            Assert.That(recordedSucceeded, Is.True);
        }

        [Test]
        [Category("Boundary")]
        public void UIManagerLogging_UsesInjectedHookOrLevelAwareUnityFallback()
        {
            var managerSource = File.ReadAllText(AssetPath("Runtime", "UI", "Core", "UIManager.cs"));
            var policyPath = AssetPath("Runtime", "UI", "Core", "Policies", "UIManagerLogPolicy.cs");

            Assert.That(File.Exists(policyPath), Is.True,
                "UIManager view-request log decisions must live outside the lifecycle/open-close flow.");
            var policySource = File.ReadAllText(policyPath);
            var logMethod = SourceTextRegionExtractor.ExtractMethodRegion(
                managerSource,
                "private void LogUIManager(UIManagerLogDecision decision)");

            Assert.That(managerSource, Does.Contain("LogUIManager(UIManagerLogPolicy.ViewNameEmpty())"));
            Assert.That(managerSource, Does.Contain("LogUIManager(UIManagerLogPolicy.ViewAlreadyRegistered(config.viewName))"));
            Assert.That(managerSource, Does.Contain("LogUIManager(UIManagerLogPolicy.ViewConfigNotFound(viewName))"));
            Assert.That(managerSource, Does.Contain("LogUIManager(UIManagerLogPolicy.SingletonViewAlreadyOpen(viewName))"));
            Assert.That(managerSource, Does.Contain("LogUIManager(UIManagerLogPolicy.ViewNotFound(viewName))"));
            Assert.That(managerSource, Does.Contain("LogUIManager(UIManagerLogPolicy.ViewPrefabLoadFailed(viewName))"));
            Assert.That(managerSource, Does.Contain("LogUIManager(UIManagerLogPolicy.ViewComponentNotFound(viewName))"));
            Assert.That(managerSource, Does.Contain("_hooks.Log(decision.Level, decision.Message);"));
            Assert.That(logMethod, Does.Contain("if (_hooks != null)"));
            Assert.That(logMethod, Does.Contain("var message = $\"[UIManager] {decision.Message}\";"));
            Assert.That(logMethod, Does.Contain("Debug.LogWarning(message, this)"));
            Assert.That(logMethod, Does.Contain("Debug.LogError(message, this)"));
            Assert.That(logMethod, Does.Contain("Debug.Log(message, this)"));
            Assert.That(logMethod, Does.Not.Contain("ZGS"));

            Assert.That(policySource, Does.Contain("ViewNameEmpty()"));
            Assert.That(policySource, Does.Contain("AddressablesLoadFailed("));
            Assert.That(policySource, Does.Not.Contain("[UIManager]"));
        }

        private static void AssertDecision(
            UIManagerLogDecision decision,
            UIManagerLogLevel expectedLevel,
            string expectedMessage)
        {
            Assert.That(decision.ShouldLog, Is.True);
            Assert.That(decision.Level, Is.EqualTo(expectedLevel));
            Assert.That(decision.Message, Is.EqualTo(expectedMessage));
            Assert.That(decision.Message, Does.Not.Contain("[UIManager]"));
        }

        private static string AssetPath(params string[] parts)
        {
            return Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "Packages",
                "com.zerogamestudio.zeroengine.ui",
                Path.Combine(parts));
        }
    }
}
