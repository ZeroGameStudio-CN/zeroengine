using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZeroEngine.Console.Unity;
using Object = UnityEngine.Object;

namespace ZeroEngine.Console.UI.Tests
{
    // Shared font assets belong to the project. The console owns only its default OS instances.
    public sealed class ConsoleFontTests
    {
        [UnityTest]
        public IEnumerator DefaultFont_IsReusedThenReleasedWithTheConsole()
        {
            var root = new GameObject("Owned OS font lifetime");
            try
            {
                var view = root.AddComponent<NativeConsole>();
                view.Open();
                var owned = view.InputField.textComponent.font;
                var builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                for (int i = 0; i < 12; i++) { view.Close(); view.Open(); }
                Assert.That(view.InputField.textComponent.font, Is.SameAs(owned));
                Object.DestroyImmediate(root);
                yield return null;
                if (owned != builtin) Assert.That(!owned, Is.True);
                Assert.That(builtin, Is.Not.Null);
            }
            finally { if (root) Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator ProjectFont_KeepsItsFaceAndMaterialAndSurvivesConsoleDestruction()
        {
            var primary = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var names = primary.fontNames;
            var material = primary.material;
            var shader = material.shader;
            primary.RequestCharactersInTexture("Wi0", 16);
            Assert.That(primary.GetCharacterInfo('W', out var expected, 16), Is.True);
            var root = new GameObject("Project font contract");
            Font resolved = null;
            try
            {
                var otherText = new GameObject("Project-owned label", typeof(UnityEngine.UI.Text)).GetComponent<UnityEngine.UI.Text>();
                otherText.transform.SetParent(root.transform);
                otherText.font = primary;
                var view = root.AddComponent<NativeConsole>();
                view.ConfigureFont(primary);
                Assert.That(view.InputField, Is.Null, "Configuring a closed console must stay lazy.");
                view.Open();
                yield return null;
                resolved = view.InputField.textComponent.font;
                Assert.That(resolved, Is.SameAs(primary));
                Assert.That(resolved.material, Is.SameAs(material));
                Assert.That(resolved.fontNames.Take(names.Length), Is.EqualTo(names));
                resolved.RequestCharactersInTexture("Wi0", 16);
                Assert.That(resolved.GetCharacterInfo('W', out var actual, 16), Is.True);
                Assert.That(actual.advance, Is.EqualTo(expected.advance), "Existing glyphs must retain the project's face.");
                Assert.That(actual.glyphWidth, Is.EqualTo(expected.glyphWidth));
                Assert.That(otherText.font, Is.SameAs(primary));
                Assert.That(primary.fontNames, Is.EqualTo(names));
                Assert.That(primary.material, Is.SameAs(material));
                Assert.That(material.shader, Is.SameAs(shader));
                for (int i = 0; i < 12; i++) { view.Close(); view.Open(); }
                Assert.That(view.InputField.textComponent.font, Is.SameAs(resolved), "Opening again must reuse the fallback and atlas.");
                Object.DestroyImmediate(root);
                yield return null;
                Assert.That(resolved, Is.Not.Null);
                Assert.That(primary, Is.Not.Null);
                Assert.That(material, Is.Not.Null);
                Assert.That(primary.fontNames, Is.EqualTo(names));
            }
            finally { if (root) Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator FontChange_UpdatesOwnedLabelsWithoutResettingDraftHistoryOrHierarchy()
        {
            var root = new GameObject("Live font change");
            var primary = Font.CreateDynamicFontFromOSFont("Courier New", 16);
            try
            {
                var view = root.AddComponent<NativeConsole>();
                view.Open();
                yield return null;
                var input = view.InputField;
                var hierarchy = view.GetComponentsInChildren<Transform>(true);
                var history = view.Host.Session.GetHistory().ToArray();
                input.text = "ze-help 中文";
                input.caretPosition = input.text.Length;
                int caret = input.caretPosition;
                view.ConfigureFont(primary);
                yield return null;
                var resolved = input.textComponent.font;
                foreach (var label in view.transform.Find("ZE Console").GetComponentsInChildren<UnityEngine.UI.Text>(true))
                    Assert.That(label.font, Is.SameAs(resolved));
                Assert.That(view.InputField, Is.SameAs(input));
                Assert.That(view.GetComponentsInChildren<Transform>(true), Is.EqualTo(hierarchy));
                Assert.That(input.text, Is.EqualTo("ze-help 中文"));
                Assert.That(input.caretPosition, Is.EqualTo(caret));
                Assert.That(view.Host.Session.GetHistory(), Is.EqualTo(history));
                view.ConfigureFont(null);
                Assert.That(input.textComponent.font, Is.Not.SameAs(resolved));
                yield return null;
                Assert.That(resolved, Is.Not.Null, "The old project font is borrowed, not owned.");
                Assert.That(primary, Is.Not.Null);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(primary); }
        }

        [UnityTest]
        public IEnumerator FontProvider_ChangesLocaleOnOpenAndRetainsTheLastFontWhenUnavailable()
        {
            var root = new GameObject("Project locale font provider");
            var first = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var second = Font.CreateDynamicFontFromOSFont("Courier New", 16);
            int calls = 0;
            Font active = first;
            try
            {
                var view = root.AddComponent<NativeConsole>();
                view.FontProvider = () => { calls++; return active; };
                view.Open();
                var old = view.InputField.textComponent.font;
                view.Open();
                Assert.That(calls, Is.EqualTo(1));
                view.Close(); active = second; view.Open();
                var current = view.InputField.textComponent.font;
                Assert.That(current, Is.Not.SameAs(old));
                Assert.That(current.fontNames.Take(second.fontNames.Length), Is.EqualTo(second.fontNames));
                view.Close(); active = null; view.Open();
                Assert.That(view.InputField.textComponent.font, Is.SameAs(current));
                view.Close(); view.FontProvider = () => throw new InvalidOperationException("Unavailable locale"); view.Open();
                Assert.That(view.IsOpen, Is.True);
                Assert.That(view.InputField.textComponent.font, Is.SameAs(current));
                Assert.That(view.Host.Session.GetOutput().Count(row => row.InvocationId == "font" && row.Level == ConsoleOutputLevel.Warning), Is.EqualTo(1));
                yield return null;
                Assert.That(old, Is.Not.Null);
                Assert.That(second, Is.Not.Null);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(second); }
        }

        [UnityTest]
        public IEnumerator UnloadedProjectFont_ReopensWithAUsableFallback()
        {
            var root = new GameObject("Unloaded locale font");
            var primary = Font.CreateDynamicFontFromOSFont("Arial", 16);
            try
            {
                var view = root.AddComponent<NativeConsole>();
                view.ConfigureFont(primary);
                view.Open();
                var old = view.InputField.textComponent.font;
                view.Close(); Object.DestroyImmediate(primary); view.Open();
                var current = view.InputField.textComponent.font;
                Assert.That(current && current != old, Is.True);
                Assert.That(current.HasCharacter('A'), Is.True);
                yield return null;
                Assert.That(!old, Is.True);
            }
            finally { Object.DestroyImmediate(root); if (primary) Object.DestroyImmediate(primary); }
        }

        [UnityTest]
        public IEnumerator DefaultFont_UsesInstalledGlyphFallbackWithoutBundledFontAssets()
        {
            var root = new GameObject("System glyph fallback");
            try
            {
                var view = root.AddComponent<NativeConsole>();
                view.Open();
                var font = view.InputField.textComponent.font;
                const string sample = "Latin é Ω Ж";
                font.RequestCharactersInTexture(sample, 16);
                foreach (char glyph in sample.Where(c => c != ' '))
                    Assert.That(font.GetCharacterInfo(glyph, out _, 16), Is.True, "Missing system glyph: " + glyph);
                if (Font.GetOSInstalledFontNames().Any(name => name == "Microsoft YaHei" || name == "Microsoft YaHei UI"))
                {
                    font.RequestCharactersInTexture("中文", 16);
                    Assert.That(font.GetCharacterInfo('中', out _, 16), Is.True, "Installed CJK fallback must be used.");
                }
                yield return null;
            }
            finally { Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator ThirdPartyOverlay_BorrowsProjectFontAndLeavesTheExternalInputUntouched()
        {
            var root = new GameObject("Overlay font ownership");
            var primary = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var names = primary.fontNames;
            try
            {
                var host = root.AddComponent<ConsoleHost>();
                var external = new GameObject("External input", typeof(UnityEngine.UI.Text)).GetComponent<UnityEngine.UI.Text>();
                external.transform.SetParent(root.transform);
                external.font = primary; external.text = "ze-";
                var overlay = root.AddComponent<ConsoleInputOverlay>();
                overlay.ConfigureFont(primary);
                overlay.Initialize(host, new ConsoleInputAccess(() => external.text, value => external.text = value, () => external.text.Length, () => true, () => false));
                var canvas = root.transform.Find("ZE completion overlay");
                var resolved = canvas.GetComponentInChildren<UnityEngine.UI.Text>(true).font;
                Assert.That(resolved, Is.SameAs(primary));
                Assert.That(resolved.fontNames.Take(names.Length), Is.EqualTo(names));
                overlay.ConfigureFont(null);
                foreach (var label in canvas.GetComponentsInChildren<UnityEngine.UI.Text>(true)) Assert.That(label.font, Is.Not.SameAs(resolved));
                Assert.That(external.font, Is.SameAs(primary));
                Assert.That(external.text, Is.EqualTo("ze-"));
                Assert.That(primary.fontNames, Is.EqualTo(names));
                Object.DestroyImmediate(overlay);
                yield return null;
                Assert.That(resolved, Is.Not.Null);
                Assert.That(!canvas, Is.True);
                Assert.That(external.font, Is.SameAs(primary));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
