using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.InputSystem.Tests
{
    public sealed class InputActionLookupTests
    {
        [Test]
        public void FindAction_ExistingAction_ReturnsAction()
        {
            var asset = InputActionTestAssetFactory.Create();

            var result = InputActionLookup.FindAction(asset, new InputActionKey("Player", "Interact"));

            Assert.IsTrue(result.Success, result.Diagnostic);
            Assert.AreEqual("Interact", result.Action.name);
            Object.DestroyImmediate(asset);
        }

        [Test]
        public void FindAction_MissingAction_ReturnsDiagnostic()
        {
            var asset = InputActionTestAssetFactory.Create();

            var result = InputActionLookup.FindAction(asset, new InputActionKey("Player", "Dodge"));

            Assert.IsFalse(result.Success);
            Assert.That(result.Diagnostic, Does.Contain("Dodge"));
            Object.DestroyImmediate(asset);
        }
    }
}
