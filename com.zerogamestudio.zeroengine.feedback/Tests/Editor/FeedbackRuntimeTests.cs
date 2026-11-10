using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
        public void DefaultCatalog_ProvidesShortTextsForThirteenLocales()
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

        [TestCase(FeedbackTextId.Uploading, "提交中…")]
        [TestCase(FeedbackTextId.Retrying, "自动重试中…")]
        [TestCase(FeedbackTextId.Uploaded, "提交成功，感谢反馈。")]
        [TestCase(FeedbackTextId.UploadFailed, "提交失败")]
        public void SubmissionCatalog_KeepsApprovedChineseStatusMapping(FeedbackTextId id, string expected)
        {
            Assert.AreEqual(expected, FeedbackTextCatalog.ResolveSubmissionStatus(id, "zh-CN"));
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
        public void TwoOutcomeMode_KeepsRetriesSilentThenReportsOneConfirmedSuccess()
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
            FeedbackStatusCoordinator.Configure(configuration);
            FeedbackStatusCoordinator.HandleUploadFailed(new FeedbackUploadFailure("current", "F503", 503));
            Assert.AreEqual(0, presenter.Count);
            Assert.AreEqual(1, FeedbackStatusCoordinator.TrackedSubmissionCount);
            FeedbackStatusCoordinator.HandleUploadSucceeded(new FeedbackUploadCompletion("current"));
            FeedbackStatusCoordinator.HandleUploadSucceeded(new FeedbackUploadCompletion("current"));
            FeedbackStatusCoordinator.HandleUploadFailed(new FeedbackUploadFailure("current", "F403"));
            FeedbackStatusCoordinator.HandleUploadSucceeded(new FeedbackUploadCompletion("old-queue"));
            CollectionAssert.AreEqual(new[] { FeedbackTextId.Uploaded }, presenter.Statuses);
            Assert.AreEqual(0, FeedbackStatusCoordinator.TrackedSubmissionCount);
        }

        // Regression: a failed network attempt must not tell the player to submit another copy.
        [TestCase("F201", 0)]
        [TestCase("F202", 0)]
        [TestCase("F204", 0)]
        [TestCase("F299", 0)]
        [TestCase("F408", 408)]
        [TestCase("F425", 425)]
        [TestCase("F429", 429)]
        [TestCase("F503", 503)]
        public void TwoOutcomeMode_RetryableFailure_RemainsPending(string code, long httpStatus)
        {
            var presenter = new RecordingPresenter();
            FeedbackStatusCoordinator.Configure(new FeedbackUiConfiguration
            {
                TextResolver = new SyntheticTextResolver(), StatusPresenter = presenter,
                ShowUploadingStatus = false, ShowUploadFailureCodes = true
            });
            FeedbackStatusCoordinator.HandleLocalResult(new FeedbackSubmissionResult("current", true, FeedbackSubmissionFailure.None));
            FeedbackStatusCoordinator.HandleUploadFailed(new FeedbackUploadFailure("current", code, httpStatus));
            Assert.AreEqual(0, presenter.Count);
            Assert.AreEqual(1, FeedbackStatusCoordinator.TrackedSubmissionCount);

            // A later definite rejection must still be visible after a silent transient error.
            FeedbackStatusCoordinator.HandleUploadFailed(new FeedbackUploadFailure("current", "F403", 403));
            FeedbackStatusCoordinator.HandleUploadFailed(new FeedbackUploadFailure("current", "F403", 403));
            CollectionAssert.AreEqual(new[] { FeedbackTextId.UploadFailed }, presenter.Statuses);
            StringAssert.EndsWith(" (F403)", presenter.Texts[0]);
        }

        [TestCase("F203", 0)]
        [TestCase("F403", 403)]
        [TestCase("F413", 413)]
        public void TwoOutcomeMode_NonRetryableFailure_KeepsCodeAndAllowsLaterRecovery(string code, long httpStatus)
        {
            var presenter = new RecordingPresenter();
            FeedbackStatusCoordinator.Configure(new FeedbackUiConfiguration
            {
                TextResolver = new SyntheticTextResolver(), StatusPresenter = presenter,
                ShowUploadingStatus = false, ShowUploadFailureCodes = true
            });
            FeedbackStatusCoordinator.HandleLocalResult(new FeedbackSubmissionResult("current", true, FeedbackSubmissionFailure.None));
            FeedbackStatusCoordinator.HandleUploadFailed(new FeedbackUploadFailure("current", code, httpStatus));
            FeedbackStatusCoordinator.HandleUploadFailed(new FeedbackUploadFailure("current", code, httpStatus));
            Assert.AreEqual(1, presenter.Count);
            StringAssert.EndsWith(" (" + code + ")", presenter.Texts[0]);
            FeedbackStatusCoordinator.HandleUploadSucceeded(new FeedbackUploadCompletion("current"));
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

        [Test]
        public void DeliveryMode_RetryThenReceipt_OnlyShowsApprovedStatusesAndPersistsNotification()
        {
            using (var queue = new IsolatedQueueScope())
            {
                var presenter = new RecordingPresenter();
                FeedbackStatusCoordinator.Configure(new FeedbackUiConfiguration
                {
                    UseDeliveryStatus = true, ShowUploadingStatus = true, ShowUploadFailureCodes = true,
                    LanguageCodeResolver = () => "zh-CN", StatusPresenter = presenter
                });
                const string id = "1234567890abcdef1234567890abcdef";
                FeedbackStatusCoordinator.HandleSubmitting();
                Assert.IsTrue(FeedbackUploadQueue.TryEnqueue(id, queue.ZipPath, "POB_v1", "Synthetic"));
                FeedbackStatusCoordinator.HandleLocalResult(new FeedbackSubmissionResult(id, true, FeedbackSubmissionFailure.None));
                int calls = 0;
                IsolatedQueueScope.Set("_tryUploadOverride",
                    (Func<FeedbackUploadQueue.PendingUpload, Action<bool>, IEnumerator>)
                    ((_, completed) => CompleteSyntheticUpload(completed, ++calls > 1)));
                Exhaust(FeedbackUploadQueue.ProcessPendingUploads());
                FeedbackStatusCoordinator.HandleUploadFailed(new FeedbackUploadFailure(id, "F403", 403));
                CollectionAssert.AreEqual(new[] { "提交中…", "自动重试中…" }, presenter.Texts);
                Exhaust(FeedbackUploadQueue.ProcessPendingUploads());
                CollectionAssert.AreEqual(new[] { "提交中…", "自动重试中…", "提交成功，感谢反馈。" }, presenter.Texts);
                IsolatedQueueScope.Set("_pendingUploads", null);
                Assert.IsTrue(FeedbackUploadQueue.GetRecentSubmissions()[0].SuccessNotified);
                FeedbackStatusCoordinator.HandleLocalResult(new FeedbackSubmissionResult("local-failure", false,
                    FeedbackSubmissionFailure.QueuePersistenceFailed));
                Assert.AreEqual("提交失败（F102）", presenter.Texts[presenter.Texts.Count - 1]);
            }
        }

        private static IEnumerator CompleteSyntheticUpload(Action<bool> complete, bool succeeded)
        {
            complete(succeeded);
            yield break;
        }

        private sealed class IsolatedQueueScope : IDisposable
        {
            private readonly Dictionary<string, object> previous = new();
            private readonly string key = "ze-feedback-ui-" + Guid.NewGuid().ToString("N");
            private readonly string root;
            private readonly string previousUrl = AnalyticsConfig.ServerUrl;
            private readonly string previousSecret = AnalyticsConfig.Secret;
            private readonly string previousUploadSecret = AnalyticsConfig.UploadSecret;
            public string ZipPath { get; }

            public IsolatedQueueScope()
            {
                foreach (string name in new[] { "_queueKeyOverride", "_pendingUploads", "_recentSubmissions",
                    "_backgroundRunning", "_isProcessing", "_persistQueueOverride", "_deleteFileOverride",
                    "_tryUploadOverride", "_tryUploadDetailedOverride", "QueuedUploadSucceeded",
                    "QueuedSubmissionSucceeded", "QueuedSubmissionFailed", "SubmissionStateChanged", "_queueReadFailed" })
                {
                    FieldInfo field = Field(name);
                    previous[name] = field.GetValue(null);
                    if (!field.FieldType.IsValueType) field.SetValue(null, null);
                }
                Set("_queueKeyOverride", key);
                Set("_isProcessing", false);
                Set("_backgroundRunning", true);
                FeedbackUploadQueue.Initialize();
                Set("_backgroundRunning", true); // Synthetic uploads only, never start a network coroutine.
                AnalyticsConfig.ServerUrl = "http://127.0.0.1:1";
                AnalyticsConfig.Secret = string.Empty;
                AnalyticsConfig.UploadSecret = "synthetic-test-secret";
                root = Path.Combine(Path.GetTempPath(), key);
                Directory.CreateDirectory(root);
                ZipPath = Path.Combine(root, "synthetic.zip");
                File.WriteAllText(ZipPath, "synthetic");
            }

            private static FieldInfo Field(string name) => typeof(FeedbackUploadQueue).GetField(name,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            public static void Set(string name, object value) => Field(name).SetValue(null, value);

            public void Dispose()
            {
                typeof(FeedbackStatusCoordinator).GetMethod("ResetRuntimeState", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, null);
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.DeleteKey(key + ".backup");
                PlayerPrefs.Save();
                foreach (var entry in previous) Set(entry.Key, entry.Value);
                AnalyticsConfig.ServerUrl = previousUrl;
                AnalyticsConfig.Secret = previousSecret;
                AnalyticsConfig.UploadSecret = previousUploadSecret;
                if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(root) == key && Directory.Exists(root)) Directory.Delete(root, true);
            }
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
