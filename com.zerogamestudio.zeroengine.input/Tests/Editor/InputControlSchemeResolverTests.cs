using NUnit.Framework;

namespace ZeroEngine.InputSystem.Tests
{
    public sealed class InputControlSchemeResolverTests
    {
        [TestCase("<Gamepad>/buttonSouth", "Gamepad")]
        [TestCase("<Keyboard>/e", "Keyboard&Mouse")]
        [TestCase("<Mouse>/leftButton", "Keyboard&Mouse")]
        public void ResolveBindingGroup_KnownControlPath_ReturnsBindingGroup(
            string controlPath,
            string expectedBindingGroup)
        {
            var result = InputControlSchemeResolver.ResolveBindingGroup(controlPath);

            Assert.IsTrue(result.Success, result.Diagnostic);
            Assert.That(result.BindingGroup, Is.EqualTo(expectedBindingGroup));
        }

        [Test]
        public void ResolveBindingGroup_UnknownControlPath_ReturnsDiagnostic()
        {
            var result = InputControlSchemeResolver.ResolveBindingGroup("<XRController>/trigger");

            Assert.IsFalse(result.Success);
            Assert.That(result.Diagnostic, Does.Contain("XRController"));
        }
    }
}
