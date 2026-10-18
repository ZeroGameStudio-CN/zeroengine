using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ZGS.Analytics;

namespace ZeroEngine.Feedback.Tests.Editor
{
    [TestFixture]
    public sealed class FeedbackRuntimeTests
    {
        [SetUp]
        public void SetUp()
        {
            MethodInfo reset = typeof(FeedbackStatusCoordinator).GetMethod(
                "ResetRuntimeState",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(reset);
            reset.Invoke(null, null);
        }

        [Test]
        public void DefaultCatalog_ProvidesNineShortTextsForThirteenLocales()
        {
            Assert.AreEqual(13, FeedbackTextCatalog.LocaleCount);
            var languages = new[]
            {
                SystemLanguage.English,
                SystemLanguage.ChineseSimplified,
                SystemLanguage.ChineseTraditional,
                SystemLanguage.Japanese,
                SystemLanguage.Korean,
                SystemLanguage.German,
                SystemLanguage.French,
                SystemLanguage.Spanish,
                SystemLanguage.Russian,
                SystemLanguage.Portuguese,
                SystemLanguage.Italian,
                SystemLanguage.Dutch,
                SystemLanguage.Polish
            };

            foreach (SystemLanguage language in languages)
            foreach (FeedbackTextId id in Enum.GetValues(typeof(FeedbackTextId)))
            {
                string text = FeedbackTextCatalog.Resolve(id, language);
                Assert.IsNotEmpty(text, $"{language}/{id}");
                Assert.LessOrEqual(text.Length, 48, $"{language}/{id}: {text}");
            }
        }

        [Test]
        public void DefaultCatalog_UnknownLanguageFallsBackToEnglish()
        {
            Assert.AreEqual(
                FeedbackTextCatalog.Resolve(FeedbackTextId.Uploading, SystemLanguage.English),
                FeedbackTextCatalog.Resolve(FeedbackTextId.Uploading, SystemLanguage.Unknown));
        }

        [TestCase("zh-CN", SystemLanguage.ChineseSimplified)]
        [TestCase("zh-TW", SystemLanguage.ChineseTraditional)]
        [TestCase("zh-Hant", SystemLanguage.ChineseTraditional)]
        [TestCase("en-US", SystemLanguage.English)]
        [TestCase("ja-JP", SystemLanguage.Japanese)]
        [TestCase("ko-KR", SystemLanguage.Korean)]
        [TestCase("de-DE", SystemLanguage.German)]
        [TestCase("fr-FR", SystemLanguage.French)]
        [TestCase("es-ES", SystemLanguage.Spanish)]
        [TestCase("ru-RU", SystemLanguage.Russian)]
        [TestCase("pt-BR", SystemLanguage.Portuguese)]
        [TestCase("it-IT", SystemLanguage.Italian)]
        [TestCase("nl-NL", SystemLanguage.Dutch)]
        [TestCase("pl-PL", SystemLanguage.Polish)]
        public void ShortFailure_UsesSelectedProjectLanguage(string code, SystemLanguage expected)
        {
            Assert.AreEqual(FeedbackTextCatalog.ResolveShortFailure(expected), FeedbackTextCatalog.ResolveShortFailure(code));
        }

        [Test]
        public void StatusCoordinator_OnlyShowsSuccessForCurrentProcessSubmission()
        {
            var presenter = new RecordingPresenter();
            FeedbackStatusCoordinator.Configure(new FeedbackUiConfiguration
            {
                TextResolver = new SyntheticTextResolver(), StatusPresenter = presenter
            });

            FeedbackStatusCoordinator.HandleUploadSucceeded(
                new FeedbackUploadCompletion("old-queue"));
            Assert.AreEqual(0, presenter.Count);

            FeedbackStatusCoordinator.HandleLocalResult(
                new FeedbackSubmissionResult("current", true, FeedbackSubmissionFailure.None));
            FeedbackStatusCoordinator.HandleUploadSucceeded(
                new FeedbackUploadCompletion("current"));
            FeedbackStatusCoordinator.HandleUploadSucceeded(
                new FeedbackUploadCompletion("current"));

            CollectionAssert.AreEqual(
                new[] { FeedbackTextId.Uploading, FeedbackTextId.Uploaded },
                presenter.Statuses);
            Assert.AreEqual(0, FeedbackStatusCoordinator.TrackedSubmissionCount);
        }

        [Test]
        public void SubmissionController_DecoratesBeforeReturningLocalFailure()
        {
            string previousUrl = AnalyticsConfig.ServerUrl;
            string previousSecret = AnalyticsConfig.Secret;
            string previousUploadSecret = AnalyticsConfig.UploadSecret;
            try
            {
                AnalyticsConfig.ServerUrl = string.Empty;
                AnalyticsConfig.Secret = string.Empty;
                AnalyticsConfig.UploadSecret = string.Empty;
                var decorator = new RecordingDecorator();
                var presenter = new RecordingPresenter();
                var controller = new FeedbackSubmissionController(new FeedbackUiConfiguration
                {
                    RequestDecorator = decorator,
                    TextResolver = new SyntheticTextResolver(), StatusPresenter = presenter
                });
                FeedbackSubmissionResult result = default;

                Exhaust(controller.Submit(
                    new FeedbackFormData { Description = "Broken" },
                    value => result = value));

                Assert.IsTrue(decorator.Called);
                Assert.AreEqual(FeedbackSubmissionFailure.NotConfigured, result.Failure);
                CollectionAssert.AreEqual(
                    new[] { FeedbackTextId.UploadFailed },
                    presenter.Statuses);
                Assert.IsFalse(controller.IsSubmitting);
            }
            finally
            {
                AnalyticsConfig.ServerUrl = previousUrl;
                AnalyticsConfig.Secret = previousSecret;
                AnalyticsConfig.UploadSecret = previousUploadSecret;
            }
        }

        // Core contract: two-outcome UX never treats queue acceptance as delivery or a retry as a new submission.
        [Test]
        public void TwoOutcomeMode_ReportsFirstFailureThenOneConfirmedSuccess()
        {
            var presenter = new RecordingPresenter();
            var configuration = new FeedbackUiConfiguration
            {
                TextResolver = new SyntheticTextResolver(), StatusPresenter = presenter,
                ShowUploadingStatus = false,
                ShowUploadFailureCodes = true
            };
            FeedbackStatusCoordinator.Configure(configuration);
            FeedbackStatusCoordinator.HandleUploadFailed(new FeedbackUploadFailure("old-queue", "F403"));
            FeedbackStatusCoordinator.HandleLocalResult(new FeedbackSubmissionResult("current", true, FeedbackSubmissionFailure.None));
            Assert.AreEqual(0, presenter.Count);
            FeedbackStatusCoordinator.HandleUploadFailed(new FeedbackUploadFailure("current", "F202"));
            StringAssert.EndsWith(" (F202)", presenter.Texts[0]);
            FeedbackStatusCoordinator.Configure(configuration);
            FeedbackStatusCoordinator.HandleUploadFailed(new FeedbackUploadFailure("current", "F503"));
            Assert.AreEqual(1, presenter.Count);
            Assert.AreEqual(1, FeedbackStatusCoordinator.TrackedSubmissionCount);
            FeedbackStatusCoordinator.HandleUploadSucceeded(new FeedbackUploadCompletion("current"));
            FeedbackStatusCoordinator.HandleUploadSucceeded(new FeedbackUploadCompletion("current"));
            FeedbackStatusCoordinator.HandleUploadFailed(new FeedbackUploadFailure("current", "F403"));
            FeedbackStatusCoordinator.HandleUploadSucceeded(new FeedbackUploadCompletion("old-queue"));
            CollectionAssert.AreEqual(new[] { FeedbackTextId.UploadFailed, FeedbackTextId.Uploaded }, presenter.Statuses);
            Assert.AreEqual(0, FeedbackStatusCoordinator.TrackedSubmissionCount);
        }

        [Test]
        public void TwoOutcomeMode_LocalPersistenceFailure_ShowsCodeWithoutTrackingAsAccepted()
        {
            var presenter = new RecordingPresenter();
            FeedbackStatusCoordinator.Configure(new FeedbackUiConfiguration { TextResolver = new SyntheticTextResolver(), StatusPresenter = presenter, ShowUploadingStatus = false, ShowUploadFailureCodes = true });
            FeedbackStatusCoordinator.HandleLocalResult(new FeedbackSubmissionResult("failed-local", false, FeedbackSubmissionFailure.QueuePersistenceFailed));
            CollectionAssert.AreEqual(new[] { FeedbackTextId.UploadFailed }, presenter.Statuses);
            StringAssert.EndsWith(" (F102)", presenter.Texts[0]);
            Assert.AreEqual(0, FeedbackStatusCoordinator.TrackedSubmissionCount);
        }

        [Test]
        public void DefaultMode_KeepsNetworkRetriesSilentForExistingConsumers()
        {
            var presenter = new RecordingPresenter();
            FeedbackStatusCoordinator.Configure(new FeedbackUiConfiguration { TextResolver = new SyntheticTextResolver(), StatusPresenter = presenter });
            FeedbackStatusCoordinator.HandleLocalResult(new FeedbackSubmissionResult("current", true, FeedbackSubmissionFailure.None));
            FeedbackStatusCoordinator.HandleUploadFailed(new FeedbackUploadFailure("current", "F403"));
            CollectionAssert.AreEqual(new[] { FeedbackTextId.Uploading }, presenter.Statuses);
        }

        private static void Exhaust(IEnumerator coroutine)
        {
            while (coroutine.MoveNext())
            {
                if (coroutine.Current is IEnumerator nested)
                    Exhaust(nested);
            }
        }

        private sealed class SyntheticTextResolver : IFeedbackTextResolver
        {
            public string Resolve(FeedbackTextId textId) => textId.ToString();
        }

        private sealed class RecordingPresenter : IFeedbackStatusPresenter
        {
            public readonly System.Collections.Generic.List<FeedbackTextId> Statuses = new();
            public readonly System.Collections.Generic.List<string> Texts = new();
            public int Count => Statuses.Count;

            public void Show(FeedbackTextId status, string text)
            {
                Statuses.Add(status);
                Texts.Add(text);
            }
        }

        private sealed class RecordingDecorator : IFeedbackRequestDecorator
        {
            public bool Called;

            public void Decorate(FeedbackSubmissionRequest request)
            {
                Called = true;
                request.UserName = "Tester";
            }
        }
    }
}
