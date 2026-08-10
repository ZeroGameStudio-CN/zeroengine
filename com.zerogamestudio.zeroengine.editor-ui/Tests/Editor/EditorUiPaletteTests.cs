using NUnit.Framework;
using System;
using UnityEngine;

namespace ZeroEngine.EditorUI.Tests.Editor
{
    public sealed class EditorUiPaletteTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void ResolveForSkin_TextAndStatusesMeetContrastContract(bool isProSkin)
        {
            EditorUiPalette palette = EditorUiPalette.ResolveForSkin(isProSkin);

            AssertContrast(palette.Text, palette.Surface, 4.5f, "Text");
            AssertContrast(palette.MutedText, palette.Surface, 4.5f, "MutedText");
            AssertContrast(palette.Accent, palette.Surface, 4.5f, "Accent");
            AssertContrast(palette.Success, palette.Surface, 4.5f, "Success");
            AssertContrast(palette.Warning, palette.Surface, 4.5f, "Warning");
            AssertContrast(palette.Error, palette.Surface, 4.5f, "Error");
            AssertContrast(palette.Border, palette.Surface, 3f, "Border");
        }

        [Test]
        public void Styles_RebuildOnFirstUseAndSkinChange()
        {
            Assert.That(EditorUiStyles.RequiresRebuild(false, false, false), Is.True);
            Assert.That(EditorUiStyles.RequiresRebuild(true, false, false), Is.False);
            Assert.That(EditorUiStyles.RequiresRebuild(true, false, true), Is.True);
            Assert.That(EditorUiStyles.RequiresRebuild(true, true, false), Is.True);
        }

        [TestCase(899f, EditorUiResponsiveMode.Compact)]
        [TestCase(900f, EditorUiResponsiveMode.Standard)]
        [TestCase(1200f, EditorUiResponsiveMode.Standard)]
        public void ResponsiveMode_UsesStableDashboardBreakpoint(float width, EditorUiResponsiveMode expected)
        {
            Assert.That(EditorUiGUILayout.ResponsiveMode(width), Is.EqualTo(expected));
        }

        [Test]
        public void ImguiPrimitives_ExposeGuiContentTooltipOverloads()
        {
            Assert.That(HasGuiContentOverload(nameof(EditorUiGUILayout.PrimaryButton)), Is.True);
            Assert.That(HasGuiContentOverload(nameof(EditorUiGUILayout.SelectionButton)), Is.True);
            Assert.That(HasGuiContentOverload(nameof(EditorUiGUILayout.ActionRow)), Is.True);
            Assert.That(HasGuiContentOverload(nameof(EditorUiGUILayout.Chip)), Is.True);
            Assert.That(HasGuiContentOverload(nameof(EditorUiGUILayout.Disclosure)), Is.True);
        }

        private static bool HasGuiContentOverload(string name)
        {
            return Array.Exists(
                typeof(EditorUiGUILayout).GetMethods(),
                method => method.Name == name &&
                          Array.Exists(method.GetParameters(), parameter => parameter.ParameterType == typeof(GUIContent)));
        }

        private static void AssertContrast(Color foreground, Color background, float minimum, string label)
        {
            float foregroundLuminance = RelativeLuminance(foreground);
            float backgroundLuminance = RelativeLuminance(background);
            float lighter = Mathf.Max(foregroundLuminance, backgroundLuminance);
            float darker = Mathf.Min(foregroundLuminance, backgroundLuminance);
            float contrast = (lighter + 0.05f) / (darker + 0.05f);
            Assert.That(contrast, Is.GreaterThanOrEqualTo(minimum), label + " contrast");
        }

        private static float RelativeLuminance(Color color)
        {
            return 0.2126f * Linearize(color.r) +
                   0.7152f * Linearize(color.g) +
                   0.0722f * Linearize(color.b);
        }

        private static float Linearize(float channel)
        {
            return channel <= 0.04045f
                ? channel / 12.92f
                : Mathf.Pow((channel + 0.055f) / 1.055f, 2.4f);
        }
    }
}
