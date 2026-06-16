using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.InputSystem.Tests
{
    public sealed class InputRebindServiceTests
    {
        [Test]
        public void Start_MissingAction_ReturnsFailureWithoutOperation()
        {
            var asset = InputActionTestAssetFactory.Create();

            var result = InputRebindService.Start(
                asset,
                new InputBindingKey("Player", "Missing", "Keyboard&Mouse"),
                InputRebindOptions.Default);

            Assert.IsFalse(result.Success);
            Assert.IsNull(result.Operation);
            Assert.That(result.Diagnostic, Does.Contain("Missing"));
            Object.DestroyImmediate(asset);
        }

        [Test]
        public void Start_ValidAction_CreatesDisposableOperation()
        {
            var asset = InputActionTestAssetFactory.Create();

            var result = InputRebindService.Start(
                asset,
                new InputBindingKey("Player", "Interact", "Keyboard&Mouse"),
                new InputRebindOptions("<Keyboard>/escape", new[] { "<Mouse>/position", "<Pointer>/delta" }));

            Assert.IsTrue(result.Success, result.Diagnostic);
            Assert.IsNotNull(result.Operation);
            result.Dispose();
            Assert.IsTrue(result.IsDisposed);
            Object.DestroyImmediate(asset);
        }
    }
}
