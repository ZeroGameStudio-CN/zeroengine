using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.UI.Tests.Editor.Core
{
    [TestFixture]
    [Category("Unit")]
    [Category("UI")]
    public sealed class UIManagerCloseAllRegressionTests
    {
        [Test]
        [Category("Boundary")]
        public void CloseAllAsync_PausedTopView_RemainsClosableAndDrainsStack()
        {
            var managerSource = File.ReadAllText(AssetPath("Runtime", "UI", "Core", "UIManager.cs"));
            var closeCore = SourceTextRegionExtractor.ExtractMethodRegion(
                managerSource,
                "private async Task CloseCoreAsync(UIViewBase view, UICloseArgs args)");
            var closeAll = SourceTextRegionExtractor.ExtractMethodRegion(
                managerSource,
                "public async Task CloseAllAsync(UILayer? layer = null)");

            Assert.That(closeCore, Does.Contain("view.State != UIViewState.Paused"));
            Assert.That(closeCore, Does.Contain("!args.Force"));
            Assert.That(closeAll, Does.Contain("while (stack.Count > 0)"));
            Assert.That(closeAll, Does.Contain("await CloseAsync(stack.Peek(), UICloseArgs.Create());"));
            AssertOrder(
                closeCore,
                "view.State != UIViewState.Paused",
                "!args.Force",
                "A paused top view must be accepted by CloseCoreAsync before the non-force early return.");
            AssertOrder(
                closeAll,
                "while (stack.Count > 0)",
                "await CloseAsync(stack.Peek(), UICloseArgs.Create());",
                "CloseAllAsync must await closing the current top before checking the stack again.");
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
            return Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "Packages",
                "com.zerogamestudio.zeroengine.ui",
                Path.Combine(parts));
        }
    }
}
