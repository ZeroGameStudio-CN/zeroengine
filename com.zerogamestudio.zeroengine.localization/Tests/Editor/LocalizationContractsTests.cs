using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace ZeroEngine.Localization.Tests
{
    public sealed class LocalizationContractsTests
    {
        [Test]
        public void InitializeAndSwitch_PreloadsBeforePublish_AndPublishesOnce()
        {
            var provider = new FakeProvider(
                new[] { "en", "zh-Hant", "zh" },
                (locale, tables) => LocalizationProviderResult.Succeeded());
            var service = new LocalizedTextService(
                provider,
                new LocalizationServiceOptions
                {
                    DefaultLocale = "en",
                    RequiredTables = new[] { "UI" }
                });
            int eventCount = 0;
            LocalizationChangedEventArgs changed = null;
            service.LocaleChanged += args =>
            {
                eventCount++;
                changed = args;
            };

            LocalizationResult initialized = service.InitializeAsync().GetAwaiter().GetResult();
            LocalizationResult switched = service.SetLocaleAsync("zh-Hant-TW").GetAwaiter().GetResult();

            Assert.That(initialized.Success, Is.True);
            Assert.That(switched.Success, Is.True);
            Assert.That(switched.EffectiveLocale, Is.EqualTo("zh-Hant"));
            Assert.That(switched.UsedFallback, Is.True);
            Assert.That(service.CurrentLocale, Is.EqualTo("zh-Hant"));
            Assert.That(eventCount, Is.EqualTo(1));
            Assert.That(changed.PreviousLocale, Is.EqualTo("en"));
            Assert.That(changed.EffectiveLocale, Is.EqualTo("zh-Hant"));
            Assert.That(provider.PreloadedLocales, Is.EqualTo(new[] { "en", "zh-Hant" }));
            Assert.That(provider.LastRequiredTables, Is.EqualTo(new[] { "UI" }));
        }

        [Test]
        public void FailedSwitch_PreservesPreviousLocale_AndCancellationIsStructured()
        {
            bool failPreload = false;
            var provider = new FakeProvider(
                new[] { "en" },
                (locale, tables) => failPreload
                    ? LocalizationProviderResult.Failed("preload-failed")
                    : LocalizationProviderResult.Succeeded());
            var service = new LocalizedTextService(provider);
            Assert.That(service.InitializeAsync().GetAwaiter().GetResult().Success, Is.True);

            failPreload = true;
            LocalizationResult failed = service.SetLocaleAsync("fr").GetAwaiter().GetResult();
            Assert.That(failed.Status, Is.EqualTo(LocalizationOperationStatus.Failed));
            Assert.That(service.CurrentLocale, Is.EqualTo("en"));

            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                LocalizationResult cancelled = service.SetLocaleAsync("en", cancellation.Token)
                    .GetAwaiter()
                    .GetResult();
                Assert.That(cancelled.Status, Is.EqualTo(LocalizationOperationStatus.Cancelled));
                Assert.That(service.CurrentLocale, Is.EqualTo("en"));
            }
        }

        [Test]
        public void GetString_UsesPlaceholderAndStructuredFormatDiagnostic()
        {
            var provider = new FakeProvider(
                new[] { "en" },
                (locale, tables) => LocalizationProviderResult.Succeeded());
            provider.Texts["UI/hello"] = "Hello {0}";
            provider.Texts["UI/broken"] = "Broken {";
            var service = new LocalizedTextService(provider);
            Assert.That(service.InitializeAsync().GetAwaiter().GetResult().Success, Is.True);

            LocalizedTextResult found = service.GetStringAsync(
                "UI",
                "hello",
                new object[] { "player" }).GetAwaiter().GetResult();
            LocalizedTextResult broken = service.GetStringAsync(
                "UI",
                "broken",
                new object[] { "secret" }).GetAwaiter().GetResult();
            LocalizedTextResult missing = service.GetStringAsync("UI", "missing")
                .GetAwaiter()
                .GetResult();

            Assert.That(found.Value, Is.EqualTo("Hello player"));
            Assert.That(broken.Status, Is.EqualTo(LocalizationOperationStatus.FormatFailed));
            Assert.That(broken.FormatDiagnostic.Code, Is.EqualTo("invalid-format"));
            Assert.That(broken.FormatDiagnostic.ParameterCount, Is.EqualTo(1));
            Assert.That(broken.Error, Does.Not.Contain("secret"));
            Assert.That(missing.Status, Is.EqualTo(LocalizationOperationStatus.Missing));
            Assert.That(missing.Value, Is.EqualTo("[missing]"));
        }

        [Test]
        public void FontRouter_UsesExactParentAndWildcardRoutes_AndRejectsDuplicates()
        {
            var router = new LocaleFontRouter<string>(value => !string.IsNullOrEmpty(value));
            Assert.That(router.TryRegister("en", "default", "Latin"), Is.True);
            Assert.That(router.TryRegister("zh", "default", "Cjk"), Is.True);
            Assert.That(router.TryRegister("*", "fallback", "Fallback"), Is.True);
            Assert.That(router.TryRegister("en", "default", "Duplicate"), Is.False);

            string font;
            Assert.That(router.TryResolve("zh-Hant-TW", "body", out font), Is.True);
            Assert.That(font, Is.EqualTo("Cjk"));
            Assert.That(router.TryResolve("fr", "fallback", out font), Is.True);
            Assert.That(font, Is.EqualTo("Fallback"));

            FontRouteValidationResult validation = LocaleFontRouteValidator.Validate(
                new[]
                {
                    new LocaleFontRoute<string>("en", "default", "Latin"),
                    new LocaleFontRoute<string>("en", "default", "Duplicate")
                },
                new[] { "en", "zh" },
                new[] { "default" },
                value => !string.IsNullOrEmpty(value));
            Assert.That(validation.IsValid, Is.False);
        }

        [Test]
        public void PlaceholderValidator_DetectsMissingAndUnexpectedTokens()
        {
            var translations = new Dictionary<string, string>
            {
                { "zh", "欢迎" },
                { "ja", "ようこそ {1}" }
            };

            LocalizationPlaceholderValidationResult result = LocalizationPlaceholderValidator.Validate(
                "Welcome {0}",
                translations);

            Assert.That(result.IsValid, Is.False);
            bool foundMissing = false;
            bool foundUnexpected = false;
            for (int i = 0; i < result.Issues.Count; i++)
            {
                LocalizationPlaceholderIssue issue = result.Issues[i];
                foundMissing |= issue.Code == LocalizationPlaceholderIssueCode.MissingPlaceholder &&
                                issue.LocaleCode == "zh";
                foundUnexpected |= issue.Code == LocalizationPlaceholderIssueCode.UnexpectedPlaceholder &&
                                   issue.LocaleCode == "ja";
            }

            Assert.That(foundMissing, Is.True);
            Assert.That(foundUnexpected, Is.True);
        }

        private sealed class FakeProvider : ILocalizationProvider
        {
            private readonly Func<string, IReadOnlyList<string>, LocalizationProviderResult> _preload;

            public FakeProvider(
                IReadOnlyList<string> availableLocales,
                Func<string, IReadOnlyList<string>, LocalizationProviderResult> preload)
            {
                AvailableLocales = availableLocales;
                _preload = preload;
            }

            public IReadOnlyList<string> AvailableLocales { get; }
            public List<string> PreloadedLocales { get; } = new List<string>();
            public IReadOnlyList<string> LastRequiredTables { get; private set; }
            public Dictionary<string, string> Texts { get; } = new Dictionary<string, string>();

            public Task<LocalizationProviderResult> InitializeAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(LocalizationProviderResult.Succeeded());
            }

            public Task<LocalizationProviderResult> PreloadAsync(
                string localeCode,
                IReadOnlyList<string> requiredTables,
                CancellationToken cancellationToken)
            {
                PreloadedLocales.Add(localeCode);
                LastRequiredTables = requiredTables;
                return Task.FromResult(_preload(localeCode, requiredTables));
            }

            public Task<LocalizationProviderTextResult> GetTextAsync(
                string localeCode,
                string tableName,
                string key,
                CancellationToken cancellationToken)
            {
                string value;
                return Task.FromResult(Texts.TryGetValue(tableName + "/" + key, out value)
                    ? LocalizationProviderTextResult.Found(value)
                    : LocalizationProviderTextResult.Missing());
            }
        }
    }
}
