using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using ZGS.Analytics;
using Object = UnityEngine.Object;

namespace ZeroEngine.Feedback.Tests.PlayMode
{
    public sealed class FeedbackPanelPlayModeTests
    {
        [UnityTest]
        public IEnumerator DeliveryStatuses_ReplaceTheVisibleToastInsteadOfStacking()
        {
            FeedbackUiConfiguration previous = FeedbackPanel.CurrentConfiguration;
            try
            {
                FeedbackPanel.Configure(new FeedbackUiConfiguration
                {
                    UseDeliveryStatus = true, ShowUploadingStatus = true, ShowUploadFailureCodes = true,
                    LanguageCodeResolver = () => "en-US"
                });
                FeedbackStatusCoordinator.HandleSubmitting();
                Assert.AreEqual(1, ZeroEngine.UI.Toast.Toast.Runtime.ActiveCount);
                FeedbackStatusCoordinator.HandleLocalResult(new ZGS.Analytics.FeedbackSubmissionResult(
                    "synthetic-visible-state", true, ZGS.Analytics.FeedbackSubmissionFailure.None));
                FeedbackStatusCoordinator.HandleUploadSucceeded(new ZGS.Analytics.FeedbackUploadCompletion("synthetic-visible-state"));
                Assert.AreEqual(1, ZeroEngine.UI.Toast.Toast.Runtime.ActiveCount);
                FeedbackStatusCoordinator.HandleLocalResult(new ZGS.Analytics.FeedbackSubmissionResult(
                    "synthetic-local-failure", false, ZGS.Analytics.FeedbackSubmissionFailure.QueuePersistenceFailed));
                Assert.AreEqual(1, ZeroEngine.UI.Toast.Toast.Runtime.ActiveCount);
                yield return null;
                Assert.AreEqual(1, Object.FindObjectsOfType<ZeroEngine.UI.Toast.ToastItemView>().Length);
            }
            finally
            {
                ZeroEngine.UI.Toast.Toast.ClearGroup("feedback-submission");
                FeedbackPanel.Configure(previous);
            }
        }

        [UnityTest]
        public IEnumerator Open_WithExistingEventSystem_CreatesAndClosesDefaultView()
        {
            var eventSystem = new GameObject("Feedback Test EventSystem", typeof(EventSystem));
            FeedbackPanel.Configure(new FeedbackUiConfiguration());

            FeedbackPanel.Open();
            yield return null;

            DefaultFeedbackPanelView view = Object.FindObjectOfType<DefaultFeedbackPanelView>();
            Assert.IsNotNull(view);
            Assert.IsFalse(view.IsSubmitting);
            Assert.IsNotNull(view.GetComponentInParent<Canvas>());

            FeedbackPanel.Close();
            yield return null;

            Assert.IsNull(Object.FindObjectOfType<DefaultFeedbackPanelView>());
            Object.Destroy(eventSystem);
        }

        [UnityTest]
        public IEnumerator SubmissionDetails_CloseRestoresFocusAndStaleOwnerCannotCloseReplacement()
        {
            Assert.IsNotNull(TMPro.TMP_Settings.defaultFontAsset, "The host must provide TMP resources.");
            var ownedSystem = EventSystem.current == null ? new GameObject("Feedback Detail EventSystem", typeof(EventSystem)) : null;
            EventSystem system = EventSystem.current;
            GameObject previous = system.currentSelectedGameObject;
            var host = new GameObject("Feedback Host Selection", typeof(RectTransform), typeof(UnityEngine.UI.Button));
            FeedbackUiConfiguration previousConfiguration = FeedbackPanel.CurrentConfiguration;
            FeedbackSubmissionHistoryView first = null;
            FeedbackSubmissionHistoryView second = null;
            try
            {
                system.SetSelectedGameObject(host);
                FeedbackPanel.Configure(new FeedbackUiConfiguration { LanguageCodeResolver = () => "en-US" });
                first = FeedbackPanel.OpenSubmissionDetails();
                yield return null;
                var scroll = first.GetComponentInChildren<UnityEngine.UI.ScrollRect>();
                Assert.IsNotNull(scroll);
                Assert.IsTrue(scroll.content.IsChildOf(scroll.viewport));
                Assert.IsNotNull(scroll.viewport.GetComponent<UnityEngine.UI.Mask>());
                Assert.IsTrue(system.currentSelectedGameObject.transform.IsChildOf(first.transform));
                second = FeedbackPanel.OpenSubmissionDetails();
                FeedbackPanel.CloseSubmissionDetails(first);
                yield return null;
                Assert.IsNotNull(second, "A replaced owner must not close the current details view.");
                FeedbackPanel.CloseSubmissionDetails(second);
                yield return null;
                Assert.AreEqual(host, system.currentSelectedGameObject);
            }
            finally
            {
                FeedbackPanel.CloseSubmissionDetails();
                FeedbackPanel.Configure(previousConfiguration);
                if (system != null) system.SetSelectedGameObject(previous);
                Object.Destroy(host);
                if (ownedSystem != null) Object.Destroy(ownedSystem);
            }
        }

        [UnityTest]
        public IEnumerator SubmissionDetails_RefreshPreservesSelectedRowActionAndScroll()
        {
            var queue = new IsolatedQueueScope();
            var ownedSystem = EventSystem.current == null ? new GameObject("Feedback Focus EventSystem", typeof(EventSystem)) : null;
            EventSystem system = EventSystem.current;
            GameObject previous = system.currentSelectedGameObject;
            FeedbackUiConfiguration configuration = FeedbackPanel.CurrentConfiguration;
            try
            {
                for (int i = 0; i < 12; i++) queue.Enqueue();
                FeedbackPanel.Configure(new FeedbackUiConfiguration { UseDeliveryStatus = true, LanguageCodeResolver = () => "en-US" });
                FeedbackSubmissionHistoryView view = FeedbackPanel.OpenSubmissionDetails();
                yield return null;
                Canvas.ForceUpdateCanvases();
                ScrollRect scroll = view.GetComponentInChildren<ScrollRect>();
                Button retry = view.GetComponentsInChildren<Button>().First(button =>
                    button.GetComponentInChildren<TMPro.TMP_Text>().text == "Retry");
                string id = retry.transform.parent.parent.GetComponentsInChildren<TMPro.TMP_Text>()
                    .Select(label => label.text).First(text => text.Length == 32);
                system.SetSelectedGameObject(retry.gameObject);
                scroll.verticalNormalizedPosition = 0.4f;
                float position = scroll.verticalNormalizedPosition;

                view.Refresh();
                yield return null;
                Assert.IsNotNull(system.currentSelectedGameObject);
                Assert.AreEqual("Retry", system.currentSelectedGameObject.GetComponentInChildren<TMPro.TMP_Text>().text);
                Assert.IsTrue(system.currentSelectedGameObject.transform.parent.parent.GetComponentsInChildren<TMPro.TMP_Text>()
                    .Any(label => label.text == id), "The same submission must retain focus after its buttons are rebuilt.");
                Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(position).Within(0.025f));
            }
            finally
            {
                FeedbackPanel.CloseSubmissionDetails();
                if (system != null) system.SetSelectedGameObject(previous);
                if (ownedSystem != null) Object.Destroy(ownedSystem);
                queue.Dispose();
                FeedbackPanel.Configure(configuration);
            }
        }

        [UnityTest]
        public IEnumerator SubmissionDetails_ReceiptMovesRetryFocusToTheSameSubmissionCopyButton()
        {
            var queue = new IsolatedQueueScope();
            var ownedSystem = EventSystem.current == null ? new GameObject("Feedback Receipt EventSystem", typeof(EventSystem)) : null;
            EventSystem system = EventSystem.current;
            GameObject previous = system.currentSelectedGameObject;
            FeedbackUiConfiguration configuration = FeedbackPanel.CurrentConfiguration;
            try
            {
                string id = queue.Enqueue();
                IsolatedQueueScope.Set("_tryUploadOverride",
                    (Func<FeedbackUploadQueue.PendingUpload, Action<bool>, IEnumerator>)
                    ((_, completed) => CompleteSyntheticUpload(completed, false)));
                yield return FeedbackUploadQueue.ProcessPendingUploads();
                Assert.IsTrue(FeedbackSubmissionService.RetrySubmission(id));
                FeedbackPanel.Configure(new FeedbackUiConfiguration { UseDeliveryStatus = true, LanguageCodeResolver = () => "en-US" });
                FeedbackSubmissionHistoryView view = FeedbackPanel.OpenSubmissionDetails();
                yield return null;
                Assert.IsTrue(view.GetComponentsInChildren<TMPro.TMP_Text>().Any(label =>
                    label.text.EndsWith("Retrying feedback…", StringComparison.Ordinal)),
                    "The dated history row must show its retry status.");
                Button retry = view.GetComponentsInChildren<Button>().First(button =>
                    button.GetComponentInChildren<TMPro.TMP_Text>().text == "Retry");
                system.SetSelectedGameObject(retry.gameObject);
                IsolatedQueueScope.Set("_tryUploadOverride",
                    (Func<FeedbackUploadQueue.PendingUpload, Action<bool>, IEnumerator>)
                    ((_, completed) => CompleteSyntheticUpload(completed)));
                yield return FeedbackUploadQueue.ProcessPendingUploads();
                yield return null;

                Assert.AreEqual(FeedbackDeliveryState.Received, FeedbackUploadQueue.GetRecentSubmissions()[0].State);
                Assert.IsNotNull(system.currentSelectedGameObject);
                Assert.AreEqual("Copy ID", system.currentSelectedGameObject.GetComponentInChildren<TMPro.TMP_Text>().text);
                Assert.IsTrue(system.currentSelectedGameObject.transform.parent.parent.GetComponentsInChildren<TMPro.TMP_Text>()
                    .Any(label => label.text == id));
                Assert.IsFalse(view.GetComponentsInChildren<Button>().Any(button =>
                    button.GetComponentInChildren<TMPro.TMP_Text>().text == "Retry"));
            }
            finally
            {
                FeedbackPanel.CloseSubmissionDetails();
                if (system != null) system.SetSelectedGameObject(previous);
                if (ownedSystem != null) Object.Destroy(ownedSystem);
                queue.Dispose();
                FeedbackPanel.Configure(configuration);
            }
        }

        [UnityTest]
        public IEnumerator DefaultForm_ScrollsWhenThePointerIsOverTheGapBetweenInputs()
        {
            var queue = new IsolatedQueueScope();
            var ownedSystem = EventSystem.current == null ? new GameObject("Feedback Scroll EventSystem", typeof(EventSystem)) : null;
            EventSystem system = EventSystem.current;
            GameObject previous = system.currentSelectedGameObject;
            FeedbackUiConfiguration configuration = FeedbackPanel.CurrentConfiguration;
            try
            {
                FeedbackPanel.Configure(new FeedbackUiConfiguration { UseDeliveryStatus = true });
                FeedbackPanel.Open();
                yield return null;
                DefaultFeedbackPanelView view = Object.FindObjectOfType<DefaultFeedbackPanelView>();
                var panel = (RectTransform)view.transform.Find("SafeArea/Panel");
                panel.sizeDelta = new Vector2(720, 500);
                Canvas.ForceUpdateCanvases();
                ScrollRect scroll = view.GetComponentInChildren<ScrollRect>();
                var description = (RectTransform)scroll.content.Find("Description");
                var pointer = new PointerEventData(system)
                {
                    position = RectTransformUtility.WorldToScreenPoint(null,
                        description.TransformPoint(new Vector3(description.rect.center.x, description.rect.yMin - 6, 0))),
                    scrollDelta = new Vector2(0, -1)
                };
                var hits = new List<RaycastResult>();
                system.RaycastAll(pointer, hits);
                Assert.IsNotEmpty(hits);
                GameObject handler = ExecuteEvents.GetEventHandler<IScrollHandler>(hits[0].gameObject);
                Assert.AreEqual(scroll.gameObject, handler, "Blank form space must route wheel input to its scroll view.");
                float before = scroll.verticalNormalizedPosition;
                ExecuteEvents.Execute(handler, pointer, ExecuteEvents.scrollHandler);
                yield return null;
                Assert.Less(scroll.verticalNormalizedPosition, before);
            }
            finally
            {
                FeedbackPanel.Close();
                if (system != null) system.SetSelectedGameObject(previous);
                if (ownedSystem != null) Object.Destroy(ownedSystem);
                queue.Dispose();
                FeedbackPanel.Configure(configuration);
            }
        }

        [UnityTest]
        public IEnumerator DefaultForm_ReleasesOnlyItsRuntimeCreatedTheme()
        {
            var queue = new IsolatedQueueScope();
            FeedbackUiConfiguration configuration = FeedbackPanel.CurrentConfiguration;
            FeedbackUiTheme supplied = ScriptableObject.CreateInstance<FeedbackUiTheme>();
            try
            {
                foreach (bool custom in new[] { false, true })
                {
                    FeedbackPanel.Configure(new FeedbackUiConfiguration { Theme = custom ? supplied : null });
                    FeedbackPanel.Open();
                    yield return null;
                    DefaultFeedbackPanelView view = Object.FindObjectOfType<DefaultFeedbackPanelView>();
                    var theme = (FeedbackUiTheme)typeof(DefaultFeedbackPanelView)
                        .GetField("_theme", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
                    FeedbackPanel.Close();
                    yield return null;
                    yield return null;
                    Assert.AreEqual(custom, theme != null, "Closing must release runtime themes and preserve supplied assets.");
                }
            }
            finally
            {
                FeedbackPanel.Close();
                Object.Destroy(supplied);
                queue.Dispose();
                FeedbackPanel.Configure(configuration);
            }
        }

        private static IEnumerator CompleteSyntheticUpload(Action<bool> completed, bool succeeded = true)
        {
            completed(succeeded);
            yield break;
        }

        private sealed class IsolatedQueueScope : IDisposable
        {
            private readonly Dictionary<string, object> previous = new();
            private readonly string key = "ze-feedback-view-" + Guid.NewGuid().ToString("N");
            private readonly string root;
            private readonly string previousUrl = AnalyticsConfig.ServerUrl;
            private readonly string previousSecret = AnalyticsConfig.Secret;
            private readonly string previousUploadSecret = AnalyticsConfig.UploadSecret;
            private readonly string zip;

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
                Set("_backgroundRunning", true);
                AnalyticsConfig.ServerUrl = "http://127.0.0.1:1";
                AnalyticsConfig.Secret = string.Empty;
                AnalyticsConfig.UploadSecret = "synthetic-test-secret";
                root = Path.Combine(Application.temporaryCachePath, key);
                Directory.CreateDirectory(root);
                zip = Path.Combine(root, "synthetic.zip");
                File.WriteAllText(zip, "synthetic");
            }

            private static FieldInfo Field(string name) => typeof(FeedbackUploadQueue).GetField(name,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            public static void Set(string name, object value) => Field(name).SetValue(null, value);
            public string Enqueue()
            {
                string id = Guid.NewGuid().ToString("N");
                Assert.IsTrue(FeedbackUploadQueue.TryEnqueue(id, zip, "Synthetic", "Feedback UI test"));
                return id;
            }

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
                string parent = Path.GetFullPath(Application.temporaryCachePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                if (Path.GetFullPath(root).StartsWith(parent, StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(root) == key && Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
