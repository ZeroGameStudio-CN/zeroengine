using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

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
    }
}
